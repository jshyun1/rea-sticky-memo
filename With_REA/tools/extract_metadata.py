#!/usr/bin/env python3
"""Read-only payload recovery derived from this session's REA CIL anchors.

Does not execute the target, fetch URLs, or verify its RSA/Authenticode signature.
Writes only new files to the caller-selected output directory.
"""
import argparse
import hashlib
import json
from pathlib import Path
import struct
import zlib


def recover(source: Path, output: Path):
    raw = source.read_bytes()
    if raw[:2] != b'MZ':
        raise ValueError('Expected PE executable')
    pe = struct.unpack_from('<I', raw, 0x3c)[0]
    if raw[pe:pe + 4] != b'PE\0\0' or struct.unpack_from('<H', raw, pe + 24)[0] != 0x10b:
        raise ValueError('Expected PE32')
    cert_offset, cert_size = struct.unpack_from('<II', raw, pe + 24 + 128)
    if not (0 < cert_offset < cert_offset + cert_size <= len(raw)):
        raise ValueError('Invalid certificate table range')
    tag = b'MSStoreTag001'  # REA #US 0x70004a45, TagExtractor..cctor
    tag_offset = raw.index(tag, cert_offset, cert_offset + cert_size)
    position = tag_offset + len(tag)
    signature_size = struct.unpack_from('<I', raw, position)[0]
    position += 4
    signature_offset = position
    position += signature_size
    compressed_size = struct.unpack_from('<I', raw, position)[0]
    position += 4
    if signature_size > 16384 or compressed_size > 16384 or position + compressed_size > cert_offset + cert_size:
        raise ValueError('Payload length outside admitted range')
    compressed = raw[position:position + compressed_size]
    inflater = zlib.decompressobj(-15)  # REA call edge to DeflateStream
    decoded = inflater.decompress(compressed, 1024 * 1024)
    if not inflater.eof or inflater.unconsumed_tail:
        raise ValueError('Payload expansion exceeds budget or stream is incomplete')
    payload = json.loads(decoded)
    findings = {
        'evidence_id': 'LOCAL-01',
        'authority': 'provided-artifact-bytes',
        'method': 'REA-derived static marker/length/raw-DEFLATE recovery; target never executed',
        'basis_evidence_id': 'ev_1c6dce445fbaa84e9f0b32671370b33de04eba9cdc4de1907a25f6b367a5e101',
        'source': str(source.resolve()), 'bytes': len(raw), 'sha256': hashlib.sha256(raw).hexdigest(),
        'certificate_table': {'offset': cert_offset, 'size': cert_size},
        'tag_offset': tag_offset, 'tag': tag.decode(),
        'signature_offset': signature_offset, 'signature_size': signature_size,
        'compressed_offset': position, 'compressed_size': compressed_size,
        'compressed_sha256': hashlib.sha256(compressed).hexdigest(),
        'decoded_size': len(decoded), 'decoded_sha256': hashlib.sha256(decoded).hexdigest(),
        'signature_verified': False,
        'observed_fields': {key: payload.get(key) for key in [
            'schema', 'productId', 'pfns', 'installerType', 'minOS', 'autoUpdate', 'defaultLocale', 'expires']},
        'localization_count': len(payload.get('localized', {})),
        'english': payload.get('localized', {}).get('en-us'),
        'limitations': ['Embedded claims only; RSA payload and Authenticode signatures are not verified.',
                        'No Store request, install, product launch, or note behavior was observed.']
    }
    output.mkdir(parents=True, exist_ok=True)
    for name, value in [('installer-payload.json', payload), ('installer-metadata.json', findings)]:
        with (output / name).open('x', encoding='utf-8') as stream:
            json.dump(value, stream, ensure_ascii=False, indent=2)
            stream.write('\n')
    return findings


if __name__ == '__main__':
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('source', type=Path)
    parser.add_argument('output', type=Path)
    args = parser.parse_args()
    print(json.dumps(recover(args.source, args.output), ensure_ascii=False, indent=2))

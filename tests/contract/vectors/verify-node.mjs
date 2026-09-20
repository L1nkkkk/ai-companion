// Independent test-only Node codec: no imports from the Python implementation.
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';

const uuid = (bytes) => {
  const h = bytes.toString('hex');
  return `${h.slice(0, 8)}-${h.slice(8, 12)}-${h.slice(12, 16)}-${h.slice(16, 20)}-${h.slice(20)}`;
};
const uuidBytes = (text) => Buffer.from(text.replaceAll('-', ''), 'hex');
const nil = '00000000-0000-0000-0000-000000000000';

function decode(b) {
  assert(b.length >= 64 && b.length <= 9664, 'frame length');
  assert.equal(b.toString('ascii', 0, 4), 'AIC1', 'magic');
  const header = {
    version: b[4], kind: b[5], channels: b[6], flags: b[7],
    session_epoch: b.readUInt32LE(8), frame_seq: b.readUInt32LE(12),
    sample_rate: b.readUInt32LE(16), stream_id: uuid(b.subarray(24, 40)),
    turn_id: uuid(b.subarray(40, 56)), segment_index: b.readUInt32LE(56),
    offset_samples: b.readUInt32LE(60),
  };
  assert.equal(header.version, 1);
  assert([1, 2].includes(header.kind));
  assert.equal(header.channels, 1);
  assert.equal(header.flags, 0);
  assert(header.session_epoch > 0);
  assert.equal(b.readUInt32LE(20), b.length - 64);
  assert.equal((b.length - 64) % 2, 0);
  assert.equal(header.sample_rate, header.kind === 1 ? 16000 : 24000);
  assert.notEqual(header.stream_id, nil);
  assert(header.offset_samples + (b.length - 64) / 2 <= 0xffffffff);
  if (header.kind === 1) {
    assert.equal(header.turn_id, nil);
    assert.equal(header.segment_index, 0);
  } else {
    assert.notEqual(header.turn_id, nil);
  }
  return { header, payload: b.subarray(64) };
}

function encode(h, payload) {
  const b = Buffer.alloc(64 + payload.length);
  b.write('AIC1');
  b[4] = h.version; b[5] = h.kind; b[6] = h.channels; b[7] = h.flags;
  for (const [field, at] of [['session_epoch', 8], ['frame_seq', 12], ['sample_rate', 16],
    ['segment_index', 56], ['offset_samples', 60]]) b.writeUInt32LE(h[field], at);
  b.writeUInt32LE(payload.length, 20);
  uuidBytes(h.stream_id).copy(b, 24);
  uuidBytes(h.turn_id).copy(b, 40);
  payload.copy(b, 64);
  decode(b);
  return b;
}

const file = process.argv[2] ?? new URL('./audio-vectors.json', import.meta.url);
const { vectors } = JSON.parse(readFileSync(file, 'utf8'));
for (const v of vectors) {
  const expected = Buffer.from(v.frame_hex, 'hex');
  const decoded = decode(expected);
  assert.deepEqual(decoded.header, v.header, v.name);
  assert.equal(decoded.payload.toString('hex'), v.payload_hex);
  assert.deepEqual(Array.from({ length: decoded.payload.length / 2 },
    (_, i) => decoded.payload.readInt16LE(i * 2)), v.pcm_s16);
  assert.equal(encode(v.header, Buffer.from(v.payload_hex, 'hex')).toString('hex'), v.frame_hex);
}
const base = Buffer.from(vectors[1].frame_hex, 'hex');
let rejected = 0;
for (const [at, value] of [[0, 0], [4, 2], [5, 0], [6, 2], [7, 1], [16, 0], [20, 3]]) {
  const bad = Buffer.from(base); bad[at] = value;
  assert.throws(() => decode(bad)); rejected++;
}
for (const bad of [base.subarray(0, 63), Buffer.concat([base, Buffer.alloc(2)]), Buffer.alloc(9665)]) {
  assert.throws(() => decode(bad)); rejected++;
}
console.log(JSON.stringify({ language: 'Node.js', version: process.version,
  vectors: vectors.length, malformedFramesRejected: rejected, status: 'passed' }));

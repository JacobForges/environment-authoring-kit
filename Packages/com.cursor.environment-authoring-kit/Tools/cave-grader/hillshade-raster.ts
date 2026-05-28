/**
 * Hillshade from elevation grid + minimal RGB PNG writer (no GDAL).
 */
import { inflateSync } from "node:zlib";
import { deflateSync } from "node:zlib";

export type ElevationGrid = {
  width: number;
  height: number;
  /** Row-major, meters NAVD88 */
  values: Float64Array;
  nodata: number;
};

function crc32(buf: Buffer): number {
  let c = 0xffffffff;
  for (let i = 0; i < buf.length; i++) {
    c ^= buf[i];
    for (let k = 0; k < 8; k++) c = c & 1 ? 0xedb88320 ^ (c >>> 1) : c >>> 1;
  }
  return (c ^ 0xffffffff) >>> 0;
}

function pngChunk(type: string, data: Buffer): Buffer {
  const typeBuf = Buffer.from(type, "ascii");
  const len = Buffer.alloc(4);
  len.writeUInt32BE(data.length, 0);
  const crcBuf = Buffer.concat([typeBuf, data]);
  const crc = Buffer.alloc(4);
  crc.writeUInt32BE(crc32(crcBuf), 0);
  return Buffer.concat([len, typeBuf, data, crc]);
}

function paethPredictor(a: number, b: number, c: number): number {
  const p = a + b - c;
  const pa = Math.abs(p - a);
  const pb = Math.abs(p - b);
  const pc = Math.abs(p - c);
  if (pa <= pb && pa <= pc) return a;
  if (pb <= pc) return b;
  return c;
}

/** Decode 8-bit RGB PNG (filter types 0–4) written by encodeRgbPng or USGS export. */
export function decodeRgbPng(buffer: Buffer): { width: number; height: number; rgb: Uint8Array } {
  let pos = 8;
  let width = 0;
  let height = 0;
  let bitDepth = 8;
  let colorType = 2;
  const idatChunks: Buffer[] = [];

  while (pos + 12 <= buffer.length) {
    const len = buffer.readUInt32BE(pos);
    const type = buffer.toString("ascii", pos + 4, pos + 8);
    const data = buffer.subarray(pos + 8, pos + 8 + len);
    if (type === "IHDR") {
      width = data.readUInt32BE(0);
      height = data.readUInt32BE(4);
      bitDepth = data[8];
      colorType = data[9];
    } else if (type === "IDAT") {
      idatChunks.push(data);
    } else if (type === "IEND") {
      break;
    }

    pos += 12 + len;
  }

  if (!width || !height || bitDepth !== 8 || colorType !== 2) {
    throw new Error("decodeRgbPng: expected 8-bit RGB PNG");
  }

  const raw = inflateSync(Buffer.concat(idatChunks));
  const rgb = new Uint8Array(width * height * 3);
  let rawOff = 0;

  for (let y = 0; y < height; y++) {
    const filter = raw[rawOff++];
    for (let x = 0; x < width; x++) {
      for (let c = 0; c < 3; c++) {
        const i = (y * width + x) * 3 + c;
        let v = raw[rawOff++];
        if (filter !== 0) {
          const left = x > 0 ? rgb[i - 3] : 0;
          const up = y > 0 ? rgb[i - width * 3] : 0;
          const upLeft = x > 0 && y > 0 ? rgb[i - width * 3 - 3] : 0;
          if (filter === 1) v = (v + left) & 0xff;
          else if (filter === 2) v = (v + up) & 0xff;
          else if (filter === 3) v = (v + Math.floor((left + up) / 2)) & 0xff;
          else if (filter === 4) v = (v + paethPredictor(left, up, upLeft)) & 0xff;
        }

        rgb[i] = v;
      }
    }
  }

  return { width, height, rgb };
}

export function encodeRgbPng(width: number, height: number, rgb: Uint8Array): Buffer {
  const rowSize = 1 + width * 3;
  const raw = Buffer.alloc(rowSize * height);
  for (let y = 0; y < height; y++) {
    const rowOff = y * rowSize;
    raw[rowOff] = 0;
    for (let x = 0; x < width; x++) {
      const i = (y * width + x) * 3;
      raw[rowOff + 1 + x * 3] = rgb[i];
      raw[rowOff + 1 + x * 3 + 1] = rgb[i + 1];
      raw[rowOff + 1 + x * 3 + 2] = rgb[i + 2];
    }
  }
  const ihdr = Buffer.alloc(13);
  ihdr.writeUInt32BE(width, 0);
  ihdr.writeUInt32BE(height, 4);
  ihdr[8] = 8;
  ihdr[9] = 2;
  ihdr[10] = 0;
  ihdr[11] = 0;
  ihdr[12] = 0;
  const signature = Buffer.from([0x89, 0x50, 0x4e, 0x47, 0x0d, 0x0a, 0x1a, 0x0a]);
  return Buffer.concat([
    signature,
    pngChunk("IHDR", ihdr),
    pngChunk("IDAT", deflateSync(raw)),
    pngChunk("IEND", Buffer.alloc(0)),
  ]);
}

export function gridFromElevations(
  width: number,
  height: number,
  samples: number[],
  nodata = -9999
): ElevationGrid {
  const values = new Float64Array(width * height);
  for (let i = 0; i < values.length; i++) {
    const v = samples[i];
    values[i] = Number.isFinite(v) ? v : nodata;
  }
  return { width, height, values, nodata };
}

/** Fill small gaps via neighbor average */
export function fillNodata(grid: ElevationGrid): ElevationGrid {
  const { width, height, values, nodata } = grid;
  const out = new Float64Array(values);
  for (let pass = 0; pass < 4; pass++) {
    for (let y = 0; y < height; y++) {
      for (let x = 0; x < width; x++) {
        const idx = y * width + x;
        if (out[idx] !== nodata) continue;
        let sum = 0;
        let n = 0;
        for (const [dx, dy] of [
          [-1, 0],
          [1, 0],
          [0, -1],
          [0, 1],
        ]) {
          const nx = x + dx;
          const ny = y + dy;
          if (nx < 0 || ny < 0 || nx >= width || ny >= height) continue;
          const v = out[ny * width + nx];
          if (v !== nodata) {
            sum += v;
            n++;
          }
        }
        if (n) out[idx] = sum / n;
      }
    }
  }
  return { width, height, values: out, nodata };
}

export function hillshadeRgb(
  grid: ElevationGrid,
  opts?: { azimuthDeg?: number; altitudeDeg?: number; zFactor?: number }
): Uint8Array {
  const az = ((opts?.azimuthDeg ?? 315) * Math.PI) / 180;
  const alt = ((opts?.altitudeDeg ?? 45) * Math.PI) / 180;
  const zFactor = opts?.zFactor ?? 2.5;
  const { width, height, values, nodata } = grid;
  const rgb = new Uint8Array(width * height * 3);

  const zenith = Math.PI / 2 - alt;
  const cosZen = Math.cos(zenith);
  const sinZen = Math.sin(zenith);

  const cell = 30; // ~30 m spacing for EPQS grid over ~0.3° bbox

  for (let y = 0; y < height; y++) {
    for (let x = 0; x < width; x++) {
      const idx = y * width + x;
      const z = values[idx];
      const base = idx * 3;
      if (z === nodata) {
        rgb[base] = 40;
        rgb[base + 1] = 40;
        rgb[base + 2] = 48;
        continue;
      }
      const zL = x > 0 ? values[idx - 1] : z;
      const zR = x < width - 1 ? values[idx + 1] : z;
      const zU = y > 0 ? values[idx - width] : z;
      const zD = y < height - 1 ? values[idx + width] : z;
      const dzdx = ((zR === nodata ? z : zR) - (zL === nodata ? z : zL)) / (2 * cell);
      const dzdy = ((zD === nodata ? z : zD) - (zU === nodata ? z : zU)) / (2 * cell);
      const slope = Math.atan(zFactor * Math.sqrt(dzdx * dzdx + dzdy * dzdy));
      let aspect = Math.atan2(dzdy, -dzdx);
      if (aspect < 0) aspect += 2 * Math.PI;
      const hs =
        255 *
        Math.max(
          0,
          Math.min(
            1,
            cosZen * Math.cos(slope) + sinZen * Math.sin(slope) * Math.cos(az - aspect)
          )
        );
      const v = Math.round(hs);
      rgb[base] = v;
      rgb[base + 1] = v;
      rgb[base + 2] = Math.round(v * 0.92);
    }
  }
  return rgb;
}

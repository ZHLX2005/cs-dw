// 生成 10 个 nx-xx 的 logo（rounded.png）+ 抽屉自身的 app 图标。
// 复用 nx-pg 模板的零依赖 logo-gen（SVG 点阵双产出），方案表与其撞色一致。
// 用法: node tools/gen-logos.mjs   （产物落 assets/logos/ 与 assets/app.ico）
import { createRequire } from 'node:module';
import { writeFileSync, mkdirSync, existsSync } from 'node:fs';
import { join, dirname } from 'node:path';
import { fileURLToPath } from 'node:url';

const require_ = createRequire(import.meta.url);
// nx-pg 的 logo-gen 无包名导出，直接按路径 import 源文件
const genPath = join(dirname(fileURLToPath(import.meta.url)), '..', '..', 'nx-pg', 'tools', 'logo-gen', 'index.mjs');
if (!existsSync(genPath)) {
  console.error(`找不到 logo-gen: ${genPath}\n请确认 nx-pg 仓库在 nx-drawer 旁边（D:\\a_js\\js_proj\\ 下）。`);
  process.exit(1);
}
const { generateAll, logoSvg, logoPng } = await import(`file://${genPath.replaceAll('\\', '/')}`);

const root = join(dirname(fileURLToPath(import.meta.url)), '..');
const outDir = join(root, 'src', 'assets', 'logos');
mkdirSync(outDir, { recursive: true });

// 撞色方案轮换：家族感来自同一几何，色彩区分来自轮换
const APPS = [
  ['nx-kv', 'vandyke'], ['nx-nx', 'mars'],    ['nx-pg', 'tiffany'],
  ['nx-sk', 'prussian'], ['nx-rp', 'hermes'], ['nx-as', 'klein'],
  ['nx-ft', 'red'],      ['nx-kn', 'mars'],   ['nx-rh', 'prussian'],
];
let done = 0;
for (const [letters, scheme] of APPS) {
  const { note, files } = generateAll(outDir, letters, scheme);
  console.log(`${letters}: ${note} -> ${files.length} files`);
  done++;
}

// 抽屉自身的 ico：取首字母 D。buildIco 在 logo-gen 里未导出，这里内联同格式容器
// （6 字节头 + 每图 16 字节目录项 + PNG 数据，Vista 起支持 PNG 直嵌）
function icoPack(images) {
  const header = Buffer.alloc(6);
  header.writeUInt16LE(0, 0);
  header.writeUInt16LE(1, 2); // type: icon
  header.writeUInt16LE(images.length, 4);
  const dirs = [];
  let offset = 6 + images.length * 16;
  const blobs = [];
  for (const img of images) {
    const d = Buffer.alloc(16);
    d[0] = img.size >= 256 ? 0 : img.size;
    d[1] = img.size >= 256 ? 0 : img.size;
    d[2] = 0; d[3] = 0;
    d.writeUInt16LE(1, 4);  // planes
    d.writeUInt16LE(32, 6); // bpp
    d.writeUInt32LE(img.png.length, 8);
    d.writeUInt32LE(offset, 12);
    offset += img.png.length;
    dirs.push(d);
    blobs.push(img.png);
  }
  return Buffer.concat([header, ...dirs, ...blobs]);
}
writeFileSync(join(root, 'src', 'assets', 'app.ico'), icoPack(
  [16, 32, 48].map(s => ({ size: s, png: logoPng('D', 'klein', { size: s }) }))
));
// 顺便给窗口 header 用一个矢量版
writeFileSync(join(outDir, 'drawer.svg'), logoSvg('D', 'klein', { size: 512, rounded: true }), 'utf8');
console.log('app.ico + drawer.svg done');

// 生成 10 个 nx-xx 的 logo + 抽屉自身的 app 图标。
// 复用 nx-pg 模板的零依赖 logo-gen（SVG 点阵双产出），方案表与其撞色一致。
//
// 产物布局（每个应用一个子目录，抽屉配置按名引用）：
//   src/assets/logos/<name>/logo-rounded.png   ← 抽屉卡片主图
//   src/assets/logos/<name>/favicon-16|32|48.png ← 托盘/小图标
//   src/assets/logos/<name>/favicon.ico
//   src/assets/app.ico                        ← 程序图标
//
// 用法: node tools/gen-logos.mjs
import { createRequire } from 'node:module';
import { writeFileSync, mkdirSync, existsSync, rmSync } from 'node:fs';
import { join, dirname } from 'node:path';
import { fileURLToPath } from 'node:url';

const root = join(dirname(fileURLToPath(import.meta.url)), '..');
const genPath = join(root, '..', 'nx-pg', 'tools', 'logo-gen', 'index.mjs');
if (!existsSync(genPath)) {
  console.error(`找不到 logo-gen: ${genPath}\n请确认 nx-pg 仓库与 cs-dw 同级（D:\\a_js\\js_proj\\ 下）。`);
  process.exit(1);
}
const { generateAll, logoSvg, logoPng } = await import(`file://${genPath.replaceAll('\\', '/')}`);

const logosRoot = join(root, 'src', 'assets', 'logos');
mkdirSync(logosRoot, { recursive: true });

// 撞色方案轮换：家族感来自同一几何，色彩区分来自轮换
const APPS = [
  ['nx-kv', 'vandyke'], ['nx-nx', 'mars'],    ['nx-pg', 'tiffany'],
  ['nx-sk', 'prussian'], ['nx-rp', 'hermes'], ['nx-as', 'klein'],
  ['nx-ft', 'red'],      ['nx-kn', 'mars'],   ['nx-rh', 'prussian'],
];

for (const [name, scheme] of APPS) {
  const dir = join(logosRoot, name);
  mkdirSync(dir, { recursive: true });
  const { note } = generateAll(dir, name, scheme);
  // 只保留运行真正用得到的：主图 + 三个尺寸的小图标 + ico。
  // svg 留着（矢量清晰、体积小），但 WPF 加载不了 svg，配置里一律指 png。
  console.log(`${name}: ${note}`);
}

// 抽屉自身的 ico：首字母 D。buildIco 在 logo-gen 里未导出，这里内联同格式容器
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
// 矢量版备用（文档/网页用）
writeFileSync(join(logosRoot, 'drawer.svg'), logoSvg('D', 'klein', { size: 512, rounded: true }), 'utf8');
console.log('app.ico + drawer.svg done');

// Original vector artwork, rendered reproducibly to the embedded game sprites.
// Install the optional asset-build dependency with npm install, then run this script.
const fs = require('fs');
const path = require('path');
const sharp = require('sharp');
const root = path.resolve(__dirname, '../src/AmongUsDogsRoles/Resources/Abilities');
const drawings = {
  Fake: `<path fill="#fff" d="M19 103V80c0-22 18-36 39-36h16c20 0 34 14 34 35v24H83V87H45v16Z"/><path fill="#b9d4e9" d="M70 54h24c15 0 20 24 5 27H72c-14 0-16-27-2-27Z"/><path fill="#fff" d="M51 45V25h-9V13h26v12h-9v20Z"/><path fill="none" stroke="#9bb0c9" d="m18 114 95 0"/>`,
  Unfake: `<path fill="#fff" d="M36 113V72c0-22 12-36 32-36s31 14 31 36v41H76V93H59v20Z"/><path fill="#b9d4e9" d="M62 48h27c16 0 17 27 1 27H63c-15 0-16-27-1-27Z"/><path fill="none" stroke="#a5ffff" d="M17 75V25m-10 13 10-15 12 15M95 17l7-8M112 35h8"/>`,
  Investigate: `<circle cx="51" cy="49" r="33" fill="#d9f9ff"/><circle cx="51" cy="49" r="24" fill="#526779"/><path d="m75 74 34 34" fill="none" stroke="#fff" stroke-width="20"/><path d="m36 45q0-15 16-15" fill="none" stroke="#fff" stroke-width="8"/>`,
  ChainLink: `<rect x="12" y="40" width="104" height="48" rx="24" fill="none" stroke="#172132" stroke-width="18"/><rect x="12" y="40" width="104" height="48" rx="24" fill="none" stroke="#d8e3ed" stroke-width="9"/>`,
  Explode: `<path fill="#fff" d="M66 10 78 39 104 24 94 52 121 61 96 76 109 105 80 94 67 122 54 94 23 107 34 78 8 66 36 53 22 24 52 37Z"/><path fill="#ffbc69" d="m67 38 9 19 23 6-21 13-11 22-10-23-22-9 23-8Z"/>`,
  Sniff: `<path fill="#fff" d="M76 22c0 20-5 28-13 41-5 8-7 15 1 20l13 4c4 16 22 18 33 8 10-9 6-23-5-26l-6-42Z"/><path fill="none" d="M80 81q8-10 17 0"/><path fill="none" stroke="#a5ffff" d="M12 43h23q13 0 10-10M8 63h30M15 83h20q12 0 10 11"/>`,
  Execute: `<path fill="#fff" d="m86 13 23 5-5 27-45 45-17-17Z"/><path fill="#ccd8e8" d="m86 13-7 33-37 27 17 17 45-45 5-27Z"/><path fill="none" stroke="#fff" d="m31 65 35 35M48 84l-24 24"/><path fill="none" stroke="#ff8e8e" d="m17 24 13 8M43 12l3 15M16 49h12"/>`,
  Release: `<path fill="none" stroke="#fff" d="M49 38c-24-16-46 12-28 31l16 16c21 18 43-5 30-24M82 69c23 16 47-12 28-32L95 22C74 4 52 29 64 46"/><path fill="none" stroke="#a5ffff" d="m62 102 43 0-12-12m12 12-12 12M36 29 23 16M78 78l11 11"/>`
};
(async()=>{for(const [name, drawing] of Object.entries(drawings)) {
  const svg=`<svg xmlns="http://www.w3.org/2000/svg" width="128" height="128" viewBox="0 0 128 128"><g stroke="#172132" stroke-width="7" stroke-linecap="round" stroke-linejoin="round">${drawing}</g></svg>`;
  fs.writeFileSync(path.join(root,name+'.svg'),svg);
  await sharp(Buffer.from(svg)).resize(128,128).png().toFile(path.join(root,name+'.png'));
}})();

// Original cartoon animation and synthesized sound; no third-party media.
// Install the optional asset-build dependency with npm install, then run this script.
const fs = require('fs');
const path = require('path');
const sharp = require('sharp');
const root = path.resolve(__dirname, '../src/AmongUsDogsRoles/Resources/Abilities');
const clamp = x => Math.max(0, Math.min(1, x));
// 36 drawings / 1.2 seconds. Match ExplosionPresentation.FrameCount.
const frameCount = 36, ink = '#282330';
const ease = x => 1-Math.pow(1-clamp(x),3);
const pt = (a,r) => `${(Math.cos(a)*r).toFixed(2)} ${(Math.sin(a)*r).toFixed(2)}`;
// Uneven, overlapping lobes, with a different silhouette for each puff.
function billow(seed=0) {
  const n=8, step=2*Math.PI/n;
  let d=`M ${pt(-step*.5, .73)}`;
  for(let i=0;i<n;i++){
    const a=i*step, r=1+.12*Math.sin(i*2.7+seed);
    d+=` C ${pt(a-step*.46,r)} ${pt(a+step*.46,r)} ${pt(a+step*.5,.73)}`;
  }
  return d+' Z';
}
function star(t) {
  const points=[];
  for(let i=0;i<20;i++){
    const a=i*Math.PI/10-.15, r=i%2 ? .28 : .8+.2*Math.sin(i*3.7);
    points.push(pt(a,r*(145+65*t)).replace(' ',','));
  }
  return points.join(' ');
}
function drawing(f) {
  const t=f/(frameCount-1), fire=ease(t/.19), cool=clamp((t-.34)/.31);
  let art='';
  const path=(d,fill,stroke=ink,width=6,extra='')=>`<path d="${d}" fill="${fill}" stroke="${stroke}" stroke-width="${width}" stroke-linejoin="round" stroke-linecap="round" ${extra}/>`;
  // A brief warm pool grounds the impact; there is no full-screen flash.
  art+=`<ellipse cy="52" rx="${52+178*fire}" ry="${14+40*fire}" fill="url(#ground)" opacity="${clamp((.62-t)/.4)}"/>`;
  // Two pressure rings travel outward, rather than swelling with the fireball.
  for(let j=0;j<2;j++){
    const p=(t-j*.085)/.44;
    if(p<0 || p>=1)continue;
    const x=24+218*ease(p), y=7+x*.24;
    art+=`<g opacity="${(1-p)*.85}" fill="none" stroke-linecap="round"><ellipse cy="52" rx="${x}" ry="${y}" stroke="#b94926" stroke-width="${12*(1-p)+2}"/><ellipse cy="49" rx="${x}" ry="${y}" stroke="#ffe7ab" stroke-width="${7*(1-p)+1}"/></g>`;
  }
  // Ballistic sparks have individual speeds and gravity, not a rigid radial fan.
  for(let j=0;j<13;j++){
    const p=clamp((t-.035)/(.6+(j%3)*.1)), a=j*2.399+.3;
    if(t<.035 || p>=1)continue;
    const travel=(105+(j%4)*28)*ease(p), x=Math.cos(a)*travel;
    const y=Math.sin(a)*travel*.68-38*Math.sin(p*Math.PI)+45*p*p;
    const length=(15+(j%3)*6)*(1-p), dx=Math.cos(a)*length, dy=Math.sin(a)*length;
    art+=`<g opacity="${clamp((1-p)*3)}">${path(`M ${x-dx} ${y-dy} Q ${x-dx*.3} ${y-dy*.5-3} ${x} ${y}`,'none',ink,7)}${path(`M ${x-dx} ${y-dy} Q ${x-dx*.3} ${y-dy*.5-3} ${x} ${y}`,'none',j%3?'#ffd168':'#fff6cc',3)}</g>`;
  }
  // Rear smoke rolls upward early. Each puff peels off and shrinks independently.
  for(let j=0;j<5;j++){
    const birth=.12+(j%3)*.035, p=clamp((t-birth)/(1-birth));
    if(t<=birth)continue;
    const a=Math.PI+(j+.35)*Math.PI/5;
    const drift=ease(p), shrink=clamp((1-p)/.36);
    const x=Math.cos(a)*(57+85*drift), y=16+Math.sin(a)*(44+83*drift)-28*p;
    const r=(39+(j%3)*9+38*ease(p/.6))*Math.sqrt(shrink);
    const fill=j%2?'#696173':'#827786';
    art+=`<g transform="translate(${x} ${y}) rotate(${j*29+p*25})" opacity="${clamp(p*10)*clamp((1-p)*5)}">`;
    art+=`<g transform="scale(${r})">${path(billow(j+2),fill,ink,5/Math.max(r,1))}</g>`;
    art+=path(`M ${-r*.43} ${-r*.2} Q ${-r*.33} ${-r*.57} ${r*.02} ${-r*.45}`,'none','#b0a0ad',4);
    art+='</g>';
  }
  // Broad, asymmetric fire silhouette; lobes roll instead of scaling a star.
  if(t<.7){
    const size=(22+158*fire)*(1-.24*cool), y=25-28*fire-29*cool;
    const squash=1+.11*Math.sin(t*17)*fire;
    const fade=clamp((.7-t)/.15);
    art+=`<g transform="translate(${-7*fire} ${y}) scale(${size*squash} ${size/squash})" opacity="${fade}">`;
    art+=path(billow(1+t*3),'#ed592b',ink,7/size);
    art+=`<g transform="translate(-.04 -.09) scale(.87 .88)">${path(billow(1+t*3),'#ff963b','none',0)}</g>`;
    // Rolling hot layers, asymmetrical to avoid a rigid target / sun silhouette.
    art+=`<g transform="translate(${-0.12+cool*.06} ${-.06-cool*.09}) rotate(${-12+t*42}) scale(.72 .79)">${path(billow(3+t*5),'#ffd35c','none',0)}</g>`;
    art+=`<g transform="translate(${-0.15+cool*.1} ${-.07+cool*.1}) rotate(${16-t*30}) scale(${.44-cool*.18} ${.5-cool*.24})">${path(billow(7+t*6),'#fff3b5','none',0)}</g>`;
    art+=path('M -.7 .29 C -.82 .04 -.63 -.16 -.49 -.13 M .46 -.48 C .68 -.36 .7 -.15 .56 -.02 M -.12 .64 Q .17 .73 .33 .53','none','#ed692b',.05);
    art+=path('M -.72 -.18 Q -.88 -.39 -.63 -.5 M .42 -.69 Q .63 -.72 .68 -.51','none','#ffc05d',4/size);
    art+='</g>';
    // Three rolling tongues peel from the lower fireball, each on its own timing.
    for(let j=0;j<3;j++){
      const p=clamp((t-.075-j*.02)/.49), r=(22+(j%2)*9+39*ease(p/.4))*(1-.47*p);
      if(t<.075+j*.02 || p>=1)continue;
      const x=(j-1)*(68+15*p), yy=47+19*Math.sin(j*2)-17*p;
      art+=`<g transform="translate(${x} ${yy}) rotate(${j*47+p*35}) scale(${r})" opacity="${clamp((1-p)*5)}">${path(billow(j),'#f67a31',ink,4/r)}<g transform="translate(-.1 -.14) scale(.72)">${path(billow(j),'#ffd064','none',0)}</g></g>`;
    }
  }
  // One snappy white-hot impact, which immediately resolves into colored fire.
  if(t<.085){
    art+=`<g opacity="${clamp((.085-t)/.035)}"><polygon points="${star(t*12)}" fill="#fff6cc" stroke="#ef883d" stroke-width="5" stroke-linejoin="round"/></g>`;
  }
  // Low soot curls spread apart, leaving the floor/bodies readable at the finish.
  for(let j=0;j<3;j++){
    const birth=.38+(j%2)*.06, p=clamp((t-birth)/(1-birth));
    if(t<=birth)continue;
    const r=(32+(j%2)*14+29*Math.sin(Math.min(1,p/.7)*Math.PI/2))*Math.sqrt(clamp((1-p)/.3));
    const x=(j-1)*(63+17*p), y=23-20*Math.sin(j*2.1)-48*p;
    art+=`<g transform="translate(${x} ${y}) rotate(${j*19-p*18})" opacity="${clamp(p*8)*clamp((1-p)*5)}"><g transform="scale(${r})">${path(billow(j+4),j%2?'#8f8090':'#736778',ink,4.5/Math.max(1,r))}</g>`;
    art+=path(`M ${-r*.43} ${-r*.06} C ${-r*.5} ${-r*.4} ${r*.04} ${-r*.51} ${r*.23} ${-r*.25}`,'none','#b9a5ae',3.5);
    art+='</g>';
  }
  return `<svg xmlns="http://www.w3.org/2000/svg" width="512" height="512" viewBox="-256 -256 512 512"><defs><radialGradient id="ground"><stop stop-color="#ffac42" stop-opacity=".7"/><stop offset="1" stop-color="#ff7739" stop-opacity="0"/></radialGradient></defs>${art}</svg>`;
}
(async()=>{
  for(let f=0;f<frameCount;f++) {
    const svg=drawing(f);
    fs.writeFileSync(path.join(root,`Blast${f}.svg`),svg);
    await sharp(Buffer.from(svg)).png().toFile(path.join(root,`Blast${f}.png`));
  }
  // Short cartoon crack / low thump / decaying rumble, deterministic and peak-limited.
  const rate=24000, n=Math.round(rate*1.05), wave=Buffer.alloc(44+n*2);
  wave.write('RIFF');wave.writeUInt32LE(36+n*2,4);wave.write('WAVEfmt ',8);wave.writeUInt32LE(16,16);
  wave.writeUInt16LE(1,20);wave.writeUInt16LE(1,22);wave.writeUInt32LE(rate,24);wave.writeUInt32LE(rate*2,28);
  wave.writeUInt16LE(2,32);wave.writeUInt16LE(16,34);wave.write('data',36);wave.writeUInt32LE(n*2,40);
  let seed=41911, low=0, phase=0;
  for(let i=0;i<n;i++) {
    const t=i/rate;seed=(Math.imul(seed,1664525)+1013904223)>>>0;
    const noise=seed/2147483648-1;low+=.075*(noise-low);
    phase+=2*Math.PI*(48+85*Math.exp(-t*13))/rate;
    const attack=Math.min(1,t/.0025), tail=clamp((1.05-t)/.14);
    const s=(.45*noise*Math.exp(-t*22)+.65*Math.sin(phase)*Math.exp(-t*7)+1.4*low*Math.exp(-t*4))*attack*tail;
    wave.writeInt16LE(Math.round(Math.tanh(s)*.83*32767),44+i*2);
  }
  fs.writeFileSync(path.join(root,'Explosion.wav'),wave);
})();

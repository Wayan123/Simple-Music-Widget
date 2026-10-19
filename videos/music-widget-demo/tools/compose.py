"""Build local, seek-safe HyperFrames compositions from the captured WPF footage."""
import json
import shutil
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
EVENTS = json.loads((ROOT / 'assets/events.json').read_text())
VARIANTS = {
    'readme': (960, 540, 18, 1, 'Musik tetap dekat.', 'Kontrol musik dari desktop Windows.'),
    'instagram': (1080, 1920, 24, 3, 'Musik jalan.\nFokus tetap.', 'Kontrol musik tanpa pindah jendela.'),
    'threads': (1080, 1350, 20, 1, 'Widget kecil.\nKontrol lengkap.', 'Untuk yang kerja sambil dengar musik.'),
    'tiktok': (1080, 1920, 22, 2, 'Masih pindah tab\nbuat pause musik?', 'Taruh kontrolnya di desktop.'),
    'youtube-shorts': (1080, 1920, 23, 2, 'Music widget\nuntuk Windows.', 'Lihat kontrolnya bekerja langsung.'),
}
for name, (width, height, duration, start, hook, subtitle) in VARIANTS.items():
    small = name == 'readme'
    feed = name == 'threads'
    title_y = 38 if small else 145 if feed else 220
    hero_y = 60 if small else 570 if feed else 850
    center_x = width / 2 if small else 490
    scale = .83 if small else 1.12
    wide_scale = .79 if small else .79
    label_y = 164 if small else 490 if feed else 730
    foot_y = 452 if small else 1010 if feed else 1390
    hook_html = hook.replace('\n', '<br>')
    labels = [('Putar & jeda', 0, 4), ('Atur volume', 4, 8.4), ('Lebarkan saat perlu', 8.4, 11.2), ('Sembunyikan sementara', 11.2, 12.3), ('Tampilkan lagi', 12.3, 16)]
    label_html = ''.join(f'<div class="step step-{i}"><span>0{i+1}</span>{text}</div>' for i, (text, _, _) in enumerate(labels))
    js_labels = '\n'.join(f'tl.set(".step-{i}", {{autoAlpha:1}}, {start+a}).set(".step-{i}", {{autoAlpha:0}}, {start+b});' for i, (_, a, b) in enumerate(labels))
    pulses = []
    pulse_js = []
    for i, event in enumerate(EVENTS):
        if event['x'] is None or event['action'].startswith('volume-'):
            continue
        pulses.append(f'<div class="pulse pulse-{i}" style="left:{event["x"]}px;top:{event["y"]}px"></div>')
        pulse_js.append(f'tl.fromTo(".pulse-{i}",{{scale:.5,opacity:1}},{{scale:1.65,opacity:0,duration:.5,ease:"power2.out",immediateRender:false}}, {start+event["time"]});')
    automation = json.dumps({"version": 1, "lanes": [{"target": "volume", "points": [{"t": 0, "v": 0}, {"t": .8, "v": .6}, {"t": duration-1.3, "v": .6}, {"t": duration, "v": 0}]}]}, separators=(",", ":")).replace('"', '&quot;')
    audio = "" if small else f'''<audio id="music" src="../../assets/demo-track.wav" data-start="0" data-duration="{duration}" data-track-index="2" data-automation="{automation}"></audio>'''
    html = f'''<!doctype html>
<html lang="id"><head><meta charset="utf-8"><title>Music Widget — {name}</title>
<script src="../../assets/gsap.min.js"></script>
<style>
@font-face{{font-family:Demo;src:url('../../assets/DemoSans-Regular.ttf');font-weight:400}}
@font-face{{font-family:Demo;src:url('../../assets/DemoSans-Bold.ttf');font-weight:700}}
*{{box-sizing:border-box}}html,body{{margin:0;width:100%;height:100%;overflow:hidden}}
#main{{position:relative;width:{width}px;height:{height}px;background:#101914;color:#f5f5f5;font-family:Demo,sans-serif;overflow:hidden}}
.stage{{position:absolute;inset:0;background:radial-gradient(ellipse at 50% 55%,#214b33 0%,#13251b 40%,#101914 74%)}}
.brand,.heading,.labels,.foot,.note{{z-index:2}}
.brand{{position:absolute;top:{18 if small else 82}px;left:{45 if small else 80}px;display:flex;align-items:center;gap:{10 if small else 20}px;font-size:{16 if small else 28}px;font-weight:700}}
.brand img{{width:{28 if small else 52}px;height:{28 if small else 52}px}}
.heading{{position:absolute;left:{45 if small else 80}px;top:{title_y}px;width:{700 if small else 850}px}}
h1{{font-size:{40 if small else 76 if feed else 82}px;line-height:1.06;letter-spacing:-1.8px;margin:{22 if small else 35}px 0 0;font-weight:700}}
.subtitle{{font-size:{18 if small else 30}px;line-height:1.4;color:#b5b5b5;margin-top:{10 if small else 24}px}}
.labels{{position:absolute;top:{label_y}px;left:{45 if small else 80}px;width:830px;height:60px}}
.step{{position:absolute;opacity:0;visibility:hidden;font-size:{21 if small else 34}px;line-height:1.4;font-weight:700}}
.step span{{color:#7ee7a2;margin-right:18px;font-weight:400}}
.hero{{position:absolute;left:{center_x-560}px;top:{hero_y}px;width:1120px;height:520px;transform-origin:50% 50%}}
video,.end-still{{position:absolute;width:1120px;height:520px;inset:0;object-fit:contain}}
.end-still{{opacity:0;visibility:hidden}}
.pulse{{position:absolute;width:32px;height:32px;margin:-16px;border:3px solid #9cf4ba;border-radius:50%;opacity:0;pointer-events:none}}
.foot{{position:absolute;top:{foot_y}px;left:{45 if small else 80}px;width:{850 if small else 820}px}}
.cta{{font-size:{24 if small else 43}px;font-weight:700;line-height:1.25;opacity:0;visibility:hidden}}
.repo{{font-size:{17 if small else 29}px;color:#b5b5b5;line-height:1.4;margin-top:{8 if small else 18}px;opacity:0;visibility:hidden}}
.note{{position:absolute;left:{45 if small else 80}px;top:{518 if small else 1195 if feed else 1580}px;color:#b5b5b5;font-size:{12 if small else 21}px}}
</style></head><body>
<div id="main" data-composition-id="main" data-width="{width}" data-height="{height}" data-duration="{duration}">
<div class="stage" data-layout-ignore></div>
<div class="brand"><img src="../../assets/icon.png" alt="">Simple Music Widget</div>
<div class="heading"><h1>{hook_html}</h1><div class="subtitle">{subtitle}</div></div>
<div class="labels">{label_html}</div>
<div class="hero" data-layout-allow-overflow>
<video id="widget" src="../../assets/widget-source.webm" data-start="{start}" data-duration="16" data-track-index="1" muted playsinline></video>
<img class="end-still" src="../../assets/widget-final.png" alt="Music Widget setelah dipulihkan">
{''.join(pulses)}
</div>
<div class="foot"><div class="cta">Gratis. Open source. Windows.</div><div class="repo">Wayan123 / Simple-Music-Widget{'' if small else '<br>Source &amp; cara pakai di GitHub.'}</div></div>
<div class="note">Rekaman aplikasi nyata · demo file lokal{' · tanpa suara' if small else ''}</div>
{audio}
</div><script>
const tl=gsap.timeline({{paused:true}});
tl.set('.hero',{{scale:{scale}}},0);
tl.from('.heading',{{y:22,opacity:0,duration:.6,ease:'power3.out'}},0);
{js_labels}
tl.to('.hero',{{scale:{wide_scale},duration:.35,ease:'power2.inOut'}},{start+8.05});
tl.to('.hero',{{scale:{scale},duration:.35,ease:'power2.inOut'}},{start+10.05});
{''.join(pulse_js)}
tl.set('.end-still',{{autoAlpha:1}},{start+16});
tl.fromTo('.cta',{{y:16,autoAlpha:0}},{{y:0,autoAlpha:1,duration:.45,ease:'power3.out',immediateRender:false}},{duration-2 if small else start+16});
tl.to('.repo',{{autoAlpha:1,duration:.35}},{duration-1.6 if small else start+16.4});
tl.set('.brand',{{opacity:1}},{duration});
window.__timelines=window.__timelines||{{}};window.__timelines.main=tl;tl.seek(0);
</script></body></html>'''
    folder = ROOT / 'platforms' / name
    folder.mkdir(parents=True, exist_ok=True)
    (folder / 'index.html').write_text(html.replace('../../assets/', 'assets/'))
    shutil.copytree(ROOT / 'assets', folder / 'assets', dirs_exist_ok=True)
    (folder / 'hyperframes.json').write_text(json.dumps({'media': {'autoProxy': False}}))
    print(f'{name}: {width}x{height}, {duration}s, real demo starts {start}s')
(ROOT / 'index.html').write_text((ROOT / 'platforms/instagram/index.html').read_text())

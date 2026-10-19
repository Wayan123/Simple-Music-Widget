"""Build local, seek-safe HyperFrames compositions from the captured WPF footage."""
import json
import shutil
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
EVENTS = json.loads((ROOT / 'assets/events.json').read_text())
VARIANTS = {
    'readme': (960, 640, 30, 1, 'Search. Play. Stay focused.', ''),
    'instagram': (1080, 1920, 36, 3, 'Your music.\nOne small widget.', 'Search YouTube without leaving your desktop.'),
    'threads': (1080, 1350, 33, 1, 'Find a track.\nKeep your flow.', 'YouTube search and playback, in a tiny widget.'),
    'tiktok': (1080, 1920, 34, 2, 'Search YouTube.\nStay in your flow.', 'Find a song. Hit play. Get back to work.'),
    'youtube-shorts': (1080, 1920, 35, 2, 'YouTube music.\nOn your desktop.', 'Real search. Real playback. One small widget.'),
}
for name, (width, height, duration, start, hook, subtitle) in VARIANTS.items():
    small = name == 'readme'
    feed = name == 'threads'
    title_y = 38 if small else 145 if feed else 220
    hero_y = -110 if small else 300 if feed else 460
    center_x = width / 2 if small else 490
    scale = .95 if small else 1.12
    wide_scale = .60 if small else .67 if feed else .79
    label_y = 112 if small else 450 if feed else 600
    foot_y = 548 if small else 1060 if feed else 1290
    hook_html = hook.replace('\n', '<br>')
    labels = [('Open YouTube search', 0, 2), ('Type a song or artist', 2, 5), ('Get live search results', 5, 9), ('Choose a track', 9, 11), ('Play. Pause. Resume.', 11, 16), ('Set your volume', 16, 20), ('Compact or expanded', 20, 23), ('Hide to the tray', 23, 365/15), ('Bring it back', 365/15, 28)]
    label_html = ''.join(f'<div class="step step-{i}"><span>0{i+1}</span>{text}</div>' for i, (text, _, _) in enumerate(labels))
    js_labels = '\n'.join(f'tl.set(".step-{i}", {{autoAlpha:1}}, {start+a}).set(".step-{i}", {{autoAlpha:0}}, {start+b});' for i, (_, a, b) in enumerate(labels))
    pulses = []
    pulse_js = []
    for i, event in enumerate(EVENTS):
        if event.get('x') is None or event['action'].startswith('volume-'):
            continue
        pulses.append(f'<div class="pulse pulse-{i}" style="left:{event["x"]}px;top:{event["y"]}px"></div>')
        pulse_js.append(f'tl.fromTo(".pulse-{i}",{{scale:.5,opacity:1}},{{scale:1.65,opacity:0,duration:.5,ease:"power2.out",immediateRender:false}}, {start+event["time"]});')
    music_start = start + 11
    intro_end = music_start + .4
    automation = json.dumps({"version": 1, "lanes": [{"target": "volume", "points": [{"t": 0, "v": 0}, {"t": .8, "v": .35}, {"t": music_start, "v": .35}, {"t": intro_end, "v": 0}]}]}, separators=(",", ":")).replace('"', '&quot;')
    song_automation = json.dumps({"version": 1, "lanes": [{"target": "volume", "points": [{"t": 0, "v": 0}, {"t": .4, "v": .6}, {"t": 1.98, "v": .6}, {"t": 2, "v": 0}, {"t": 3, "v": 0}, {"t": 3.08, "v": .6}, {"t": 5.98, "v": .6}, {"t": 6.08, "v": .4}, {"t": duration-music_start-1, "v": .4}, {"t": duration-music_start, "v": 0}]}]}, separators=(",", ":")).replace('"', '&quot;')
    audio = "" if small else f'''<audio id="intro-music" src="../../assets/demo-track.wav" data-start="0" data-duration="{intro_end}" data-track-index="2" data-automation="{automation}"></audio><audio id="youtube-song" src="../../assets/hiraeth-demo.m4a" data-start="{music_start}" data-duration="{duration-music_start}" data-track-index="3" data-automation="{song_automation}"></audio>'''
    html = f'''<!doctype html>
<html lang="en"><head><meta charset="utf-8"><title>Music Widget — {name}</title>
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
h1{{font-size:{34 if small else 68 if feed else 82}px;line-height:1.06;letter-spacing:-1.8px;margin:{22 if small else 35}px 0 0;font-weight:700}}
.subtitle{{font-size:30px;line-height:1.4;color:#b5b5b5;margin-top:24px;display:{'none' if small else 'block'}}}
.labels{{position:absolute;top:{label_y}px;left:{45 if small else 80}px;width:830px;height:60px}}
.step{{position:absolute;opacity:0;visibility:hidden;font-size:{21 if small else 34}px;line-height:1.4;font-weight:700}}
.step span{{color:#7ee7a2;margin-right:18px;font-weight:400}}
.hero{{position:absolute;left:{center_x-560}px;top:{hero_y}px;width:1120px;height:960px;transform-origin:50% 50%}}
video,.end-still{{position:absolute;width:1120px;height:960px;inset:0;object-fit:contain}}
.end-still{{opacity:0;visibility:hidden}}
.pulse{{position:absolute;width:32px;height:32px;margin:-16px;border:3px solid #9cf4ba;border-radius:50%;opacity:0;pointer-events:none}}
.foot{{position:absolute;top:{foot_y}px;left:{45 if small else 80}px;width:{850 if small else 820}px}}
.cta{{font-size:{24 if small else 43}px;font-weight:700;line-height:1.25;opacity:0;visibility:hidden}}
.repo{{font-size:{17 if small else 29}px;color:#b5b5b5;line-height:1.4;margin-top:{8 if small else 18}px;opacity:0;visibility:hidden}}
.note{{position:absolute;left:{45 if small else 80}px;top:{620 if small else 1270 if feed else 1470}px;color:#b5b5b5;font-size:{12 if small else 18}px}}
</style></head><body>
<div id="main" data-composition-id="main" data-width="{width}" data-height="{height}" data-duration="{duration}">
<div class="stage" data-layout-ignore></div>
<div class="brand"><img src="../../assets/icon.png" alt="">Simple Music Widget</div>
<div class="heading"><h1>{hook_html}</h1><div class="subtitle">{subtitle}</div></div>
<div class="labels">{label_html}</div>
<div class="hero" data-layout-allow-overflow>
<video id="widget" src="../../assets/widget-source.webm" data-start="{start}" data-duration="28" data-track-index="1" muted playsinline></video>
<img class="end-still" src="../../assets/widget-final.png" alt="Music Widget playing a YouTube search result">
{''.join(pulses)}
</div>
<div class="foot"><div class="cta">Free. Open source. Windows.</div><div class="repo">Wayan123 / Simple-Music-Widget{'' if small else '<br>Find the source and setup guide on GitHub.'}</div></div>
<div class="note">Live YouTube demo · Network waits shortened{' · Silent GIF' if small else '<br>Music: Hiraeth — Scott Buckley · CC BY 4.0 · Edited excerpt'}</div>
{audio}
</div><script>
const tl=gsap.timeline({{paused:true}});
tl.set('.hero',{{scale:{wide_scale}}},0);
tl.from('.heading',{{y:22,opacity:0,duration:.6,ease:'power3.out'}},0);
{js_labels}
tl.to('.hero',{{scale:{scale},duration:.25,ease:'power2.inOut'}},{start+20.05});
tl.to('.hero',{{scale:{wide_scale},duration:.2,ease:'power2.inOut'}},{start+20.8});
tl.to('.hero',{{scale:{scale},duration:.25,ease:'power2.inOut'}},{start+22.05});
{''.join(pulse_js)}
tl.set('.end-still',{{autoAlpha:1}},{start+27});
tl.set('.hero',{{scale:{wide_scale}}},{start+27});
tl.set('.step',{{autoAlpha:0}},{start+27});
tl.fromTo('.cta',{{y:16,autoAlpha:0}},{{y:0,autoAlpha:1,duration:.45,ease:'power3.out',immediateRender:false}},{duration-2 if small else start+28});
tl.to('.repo',{{autoAlpha:1,duration:.35}},{duration-1.6 if small else start+28.4});
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

#!/usr/bin/env python3
"""Original procedural sound assets. Шелестов Максим Евгеньевич,2026; CC0. No samples or model calls."""
import math,random,struct,wave
from pathlib import Path
out=Path(__file__).resolve().parents[1]/'src/Client.Godot/audio';out.mkdir(parents=True,exist_ok=True)
rate=22050

def write(name,seconds,signal):
 with wave.open(str(out/(name+'.wav')),'wb') as f:
  f.setparams((1,2,rate,0,'NONE','not compressed'))
  f.writeframes(b''.join(struct.pack('<h',round(max(-1,min(1,signal(i/rate)))*32767)) for i in range(round(seconds*rate))))
for name,duration,freq in [('click',.07,740),('harvest',.16,320),('build',.25,520),('trade',.28,660),('important',.35,880)]:
 write(name,duration,lambda t,d=duration,h=freq:.22*math.sin(math.pi*t/d)**2*(math.sin(2*math.pi*h*t)+.25*math.sin(2*math.pi*h*1.5*t))/1.25)
# Seamless quiet synthetic breeze: integer harmonics of the8-second loop, no borrowed recording.
write('ambience',8,lambda t:.07*(math.sin(2*math.pi*79*t)+.3*math.sin(2*math.pi*113*t))*(.7+.3*math.sin(2*math.pi*t/8)))
# Optional original16-second arpeggio; endpoints fade to zero. Disabled by default.
notes=[261.6256,329.6276,391.9954,329.6276,293.6648,349.2282,440,349.2282]
def music(t):
 local=t%2;freq=notes[int(t//2)%len(notes)]
 return .10*math.sin(math.pi*local/2)**2*(math.sin(2*math.pi*freq*local)+.2*math.sin(2*math.pi*freq*2*local))
write('music',16,music)

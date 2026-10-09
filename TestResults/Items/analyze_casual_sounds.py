from pathlib import Path
import numpy as np
import wave

import json

root=Path('Assets/Casual Game Sounds U6/CasualGameSounds')
rows=[]
for path in sorted(root.glob('*.wav')):
    with wave.open(str(path),'rb') as w:
        sr=w.getframerate(); channels=w.getnchannels(); width=w.getsampwidth(); raw=w.readframes(w.getnframes())
    if width==3:
        packed=np.frombuffer(raw,dtype=np.uint8).reshape(-1,3).astype(np.int32)
        data=packed[:,0]+(packed[:,1]<<8)+(packed[:,2]<<16)
        data=(data^0x800000)-0x800000
    else:data=np.frombuffer(raw,dtype='<i2' if width==2 else '<i4')
    data=data.reshape(-1,channels).astype(float)/(2**(width*8-1))
    if data.ndim>1:data=data.mean(axis=1)
    hop=max(1,int(sr*.01)); win=max(1,int(sr*.02))
    env=np.array([np.sqrt(np.mean(data[i:i+win]**2)) for i in range(0,len(data),hop)])
    active=np.flatnonzero(env>env.max()*.08)
    a,b=(active[0]*hop,min(len(data),(active[-1]+2)*hop)) if len(active) else (0,len(data))
    x=data[a:b]
    frames=np.array([np.pad(x[i:i+1024],(0,max(0,1024-len(x[i:i+1024])))) for i in range(0,len(x),256)])
    z=np.fft.rfft(frames*np.hanning(1024),axis=1).T; f=np.fft.rfftfreq(1024,1/sr)
    power=abs(z)**2
    centroid=float((power*f[:,None]).sum()/max(power.sum(),1e-20))
    dominance=np.max(power,axis=0)/np.maximum(power.sum(axis=0),1e-20)
    mask=power.sum(axis=0)>power.sum(axis=0).max()*.1
    peaks=f[np.argmax(power,axis=0)][mask]
    rising=float(np.median(peaks[-max(1,len(peaks)//3):])-np.median(peaks[:max(1,len(peaks)//3)])) if len(peaks) else 0
    attacks=np.where((env[1:-1]>env[:-2]) & (env[1:-1]>env[2:]) & (env[1:-1]>env.max()*.4))[0]
    rows.append(dict(file=path.name,duration=round(len(data)/sr,3),active=round(len(x)/sr,3),rms_db=round(20*np.log10(max(np.sqrt(np.mean(x*x)),1e-10)),1),peak_db=round(20*np.log10(max(abs(x).max(),1e-10)),1),centroid_hz=round(centroid),tonality=round(float(np.mean(dominance[mask])),2),pitch_change_hz=round(rising),attacks=len(attacks)))
out=Path('TestResults/Items/CasualSoundAnalysis');out.mkdir(exist_ok=True)
(out/'analysis.json').write_text(json.dumps(rows,indent=2),encoding='utf-8')
for row in rows:print(row)


from pathlib import Path
from PIL import Image, ImageDraw
import io, struct
root=Path(__file__).resolve().parent
entries=[]
for rid,size,mono in [(101,16,False),(102,32,False),(103,16,True),(104,32,True)]:
    scale=4
    bg=(255,255,255) if mono else (255,0,255)
    strip=Image.new('RGB',(7*size,size),bg)
    for k in range(7):
        im=Image.new('RGB',(size*scale,size*scale),bg)
        d=ImageDraw.Draw(im)
        blue=(0,0,0) if mono else (26,93,165)
        green=(0,0,0) if mono else (23,140,78)
        red=(0,0,0) if mono else (200,48,48)
        def box(coords,fill=None):d.rectangle(tuple(int(v*size*scale/32) for v in coords),outline=blue,fill=fill,width=max(1,int(size*scale/16)))
        def line(coords,color=blue):d.line([(int(x*size*scale/32),int(y*size*scale/32)) for x,y in coords],fill=color,width=max(2,int(size*scale/12)))
        if k==0:
            line([(4,3),(4,28),(29,28)])
            line([(5,24),(13,10),(20,20),(29,5)])
            line([(16,3),(16,28)],red)
        elif k==1:d.polygon([(7*size*scale/32,3*size*scale/32),(29*size*scale/32,16*size*scale/32),(7*size*scale/32,29*size*scale/32)],fill=green)
        elif k==2:box((5,5,27,27),red)
        elif k==3:box((3,4,29,28));line([(4,9),(28,9)]);line([(19,13),(26,13),(26,20)]);line([(17,22),(26,13)])
        elif k==4:box((3,4,29,28));line([(4,9),(28,9)]);line([(8,16),(24,16)])
        elif k==5:box((3,4,29,28));line([(4,9),(28,9)]);box((14,17,26,25),blue)
        else:box((4,3,28,29),blue);box((8,3,22,12),bg);box((9,19,23,29),bg)
        if mono:im=im.resize((size,size)).convert('L').point(lambda x:255 if x>128 else 0).convert('RGB')
        else:im=im.resize((size,size),Image.Resampling.LANCZOS)
        strip.paste(im,(k*size,0))
    if mono:strip=strip.convert('1')
    stream=io.BytesIO();strip.save(stream,format='BMP');dib=stream.getvalue()[14:]
    header=struct.pack('<IIHHHHIHHII',len(dib),32,0xffff,2,0xffff,rid,0,0x1030,0,0,0)
    entries.append(header+dib+b'\0'*((-len(dib))%4))
null=struct.pack('<IIHHHHIHHII',0,32,0xffff,0,0xffff,0,0,0,0,0,0)
(root/'MechCue.res').write_bytes(null+b''.join(entries))

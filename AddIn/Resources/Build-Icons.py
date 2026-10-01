from pathlib import Path
from PIL import Image, ImageDraw
import io, struct
root=Path(__file__).resolve().parent
entries=[]
for command in range(36):
 for base,size,mono in [(101,16,False),(102,32,False),(103,16,True),(104,32,True)]:
    rid=base+command*10
    scale=4
    bg=(255,255,255)
    strip=Image.new('RGB',(size,size),bg)
    for k in [command]:
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
        elif k in (6,11):box((4,3,28,29),blue);box((8,3,22,12),bg);box((9,19,23,29),bg)
        elif k in (7,8,31):
            box((8,11,24,21));line([(12,4),(12,11)]);line([(20,4),(20,11)]);line([(16,21),(16,29)])
            if k==8:line([(4,28),(28,4)],red)
            if k==31:line([(3,5),(6,8),(3,11)]);line([(26,21),(29,24),(26,27)])
        elif k in (9,20):line([(8,14),(3,14),(3,5)]);line([(3,14),(12,4),(25,7),(28,20),(20,28)])
        elif k in (10,12,13):
            box((3,7,29,27));line([(3,7),(11,7),(11,3),(20,3),(20,7)])
            line([(16,10),(16,23)],green);line([(11,18),(16,23),(21,18)],green)
        elif k==15:box((3,8,17,24));box((15,4,29,20),red)
        elif k==16:line([(5,12),(5,5),(26,5),(26,13)]);line([(26,20),(26,27),(5,27),(5,20)]);line([(22,10),(26,14),(30,10)])
        elif k in (17,18,19,23,32,33,34):
            box((3,4,29,28));line([(4,10),(28,10)])
            if k in (32,33):line([(12 if k==32 else 20,10),(12 if k==32 else 20,28)])
            elif k==34:line([(4,20),(28,20)])
            elif k==23:line([(10,24),(22,12)],green)
            else:line([(7,23),(13,14),(20,20),(26,12)],green)
        elif k==21:box((5,3,27,29));line([(11,10),(17,6),(22,10),(17,15),(17,19)]);box((16,24,18,26),blue)
        elif k==22:line([(16,4),(16,28)],green);line([(4,16),(28,16)],green)
        elif k in (24,25,26,27,28,29,30):
            box((4,4,28,28));line([(16,2),(16,30)]);line([(2,16),(30,16)])
            if k==28:line([(7,18),(13,24),(25,9)],green)
            if k==29:line([(5,27),(27,5)],red)
            if k==30:line([(4,24),(12,7),(21,7),(28,24)],green)
        elif k==35:box((6,4,13,28),blue);box((19,4,26,28),blue)
        else:box((4,3,28,29),blue)
        if mono:im=im.resize((size,size)).convert('L').point(lambda x:255 if x>128 else 0).convert('RGB')
        else:im=im.resize((size,size),Image.Resampling.LANCZOS)
        strip.paste(im,(0,0))
    if mono:strip=strip.convert('1')
    stream=io.BytesIO();strip.save(stream,format='BMP');dib=stream.getvalue()[14:]
    header=struct.pack('<IIHHHHIHHII',len(dib),32,0xffff,2,0xffff,rid,0,0x1030,0,0,0)
    entries.append(header+dib+b'\0'*((-len(dib))%4))
null=struct.pack('<IIHHHHIHHII',0,32,0xffff,0,0xffff,0,0,0,0,0,0)
(root/'MechCue.res').write_bytes(null+b''.join(entries))

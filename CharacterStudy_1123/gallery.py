from PIL import Image,ImageDraw
from pathlib import Path
p=Path(__file__).parent
names=['amemori_sumiha','roen','yeh_chengxi']
o=Image.new('RGB',(1056,1060),'#424753');d=ImageDraw.Draw(o)
for i,n in enumerate(names):
    for j,k in enumerate(['front','quarter','side','rear']):
        im=Image.open(p/'work'/(n+'_'+k+'.png'));im.thumbnail((260,306));o.paste(im,(264*j,350*i+30))
    d.text((15,350*i+10),n.replace('_',' ').upper(),fill='white')
o.save(p/'turntables.jpg',quality=94)

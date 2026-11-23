from pathlib import Path
import numpy as np
from PIL import Image,ImageDraw,ImageFont
root=Path(__file__).resolve().parent
names=('amemori_sumiha','roen','yeh_chengxi')
views=[('front','正面'),('opposite','左斜側'),('side','側面'),('high','俯視'),('low','仰視背面'),('clay','無貼圖')]
font=ImageFont.truetype('C:/Windows/Fonts/msjh.ttc',19)
sheet=Image.new('RGB',(len(views)*244,1014),'#424753')
draw=ImageDraw.Draw(sheet)
for col,(_,caption) in enumerate(views):
    draw.text((244*col+12,6),caption,font=font)
for row,name in enumerate(names):
    draw.text((10,32+row*326),name.replace('_',' ').title(),font=font)
    for col,(key,_) in enumerate(views):
        im=Image.open(root/'work'/(name+'_'+key+'.png'))
        im.thumbnail((244,285))
        sheet.paste(im,(col*244,60+row*326))
sheet.save(root/'turntables.jpg',quality=94)
comparison=Image.new('RGB',(1080,1380),'#343944')
draw=ImageDraw.Draw(comparison)
for j,title in enumerate(('指定參考圖','本次 Blender 模型','現版 ACG · Unity 截圖')):
    draw.text((360*j+22,10),title,font=font)
reference=Path('C:/Users/user/Documents/app/cosmos_chimera_BGM/image')
for row,name in enumerate(names):
    paths=(next(reference.glob('*'+name+'_front*')),root/'work'/(name+'_front.png'),root.parent/'Logs'/('acg-chr_'+name+'-front.png'))
    for col,path in enumerate(paths):
        im=Image.open(path).convert('RGB')
        a=np.asarray(im).astype(float)
        yy,xx=np.where(np.abs(a-np.median(a[:10],axis=(0,1))).max(axis=2)>22)
        im=im.crop((xx.min(),yy.min(),xx.max()+1,yy.max()+1));im.thumbnail((345,402))
        comparison.paste(im,(360*col+(360-im.width)//2,49+443*row+(402-im.height)//2))
    draw.text((8,450+443*row),name.replace('_',' ').title(),font=font)
comparison.save(root/'comparison.jpg',quality=95)

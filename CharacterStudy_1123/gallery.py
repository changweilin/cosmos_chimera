from pathlib import Path
import numpy as np
from PIL import Image,ImageDraw,ImageFont
root=Path(__file__).resolve().parent
names=('amemori_sumiha','roen','yeh_chengxi')
views=[('front','正面'),('matched','角色左側 62°'),('opposite','角色右側 62°'),('side','角色左側 90°'),('sideReverse','角色右側 90°'),('training','替換服裝'),('body','基礎人體')]
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

# Match the illustrated side and display direction before inspecting each oblique view.
side_sheet=Image.new('RGB',(1120,1480),'#424753')
draw=ImageDraw.Draw(side_sheet)
for col,title in enumerate(('原畫側面','模型側面 90°','前斜側 62°','後斜側 118°')):
    draw.text((col*280+16,8),title,font=font)
for row,name in enumerate(names):
    mirrored=name=='amemori_sumiha'
    view_keys=('profile','sideReverse' if mirrored else 'side','opposite' if mirrored else 'matched','sideRear')
    draw.text((14,38+row*480),name.replace('_',' ').title(),font=font)
    for col,key in enumerate(view_keys):
        if key=='profile':
            im=Image.open(root/'work'/(name+'profile.png')).convert('RGB')
            mask=np.load(root/'work'/(name+'-masks.npz'))['side']
            im.paste('#424753',(0,0,im.width,im.height),Image.fromarray(255*(1-mask)))
        else:
            im=Image.open(root/'work'/(name+'_'+key+'.png')).convert('RGB')
            a=np.asarray(im).astype(float)
            ys,xs=np.where(np.abs(a-np.median(a[:10],axis=(0,1))).max(axis=2)>22)
            im=im.crop((xs.min(),ys.min(),xs.max()+1,ys.max()+1))
            if mirrored:im=im.transpose(Image.Transpose.FLIP_LEFT_RIGHT)
        im.thumbnail((276,443))
        side_sheet.paste(im,(col*280+(280-im.width)//2,68+row*480+(443-im.height)//2))
side_sheet.save(root/'profile-review.jpg',quality=96)

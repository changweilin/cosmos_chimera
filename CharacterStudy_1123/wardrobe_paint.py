"""Circumferential surface paint for unobserved sides and removable wardrobe parts."""
import numpy as np

def fade(x):
    t=np.clip(x,0,1)
    return t*t*(3-2*t)

def paint(name,label,style,angle,v,z,front,back,axis):
    a=np.abs(angle)
    def rgb(hexcode):
        return np.array(tuple(bytes.fromhex(hexcode))[::-1],float)
    ink=rgb('323041')
    bone=rgb('eddcd5')
    gold=rgb('dca65e')
    accent=rgb({'roen':'e89a3d','amemori_sumiha':'c45b48','yeh_chengxi':'e07744'}[name])
    navy=rgb({'roen':'45434e','amemori_sumiha':'40344c','yeh_chengxi':'38425f'}[name])
    skin=rgb('f2c9ae')
    hair=rgb({'roen':'a18673','amemori_sumiha':'51394f','yeh_chengxi':'473a46'}[name])
    face=style=='face'
    is_hair=style=='hair'
    base=navy
    if label in ('Jacket','SailorCap') or 'Sleeve' in label:base=bone
    if label.endswith('Boot'):base=rgb('524553')
    if label.endswith('Glove'):base=ink
    if style=='ear' or face or style=='skin':base=skin
    if is_hair:base=hair
    if style=='lining':base=rgb('dc987d')
    if style=='ribbon':base=accent
    if style=='metal':base=gold
    if style=='headset':base=ink
    if style=='teal':base=rgb('5bcdd1')
    fabric=np.broadcast_to(base,(len(v),angle.size,3)).copy()
    def stripe(mask,color):
        nonlocal fabric
        fabric=np.where(mask[...,None],color,fabric)
    if is_hair:
        streak=np.cos(angle*9+1.3*v)+.45*np.cos(angle*17-1.9*v)
        fabric*=1+.078*streak[...,None]
    elif style=='sport':
        stripe(v>.88,bone);stripe((v>.81)&(v<.84),accent)
        stripe((np.abs(np.cos(angle))<.26)&(v<.80),navy*.69)
        stripe((np.abs(np.cos(angle))<.055)&(v<.80),accent)
        stripe(v<.06,bone)
    elif label.endswith('Sock'):
        stripe((v>.09)&(v<.14),accent)
    elif label=='SailorCap':
        stripe(v>.67,navy);stripe((v>.68)&(v<.72),gold)
    elif label.endswith('Boot'):
        stripe(v>.90,ink);stripe((v>.80)&(v<.90),bone)
        stripe((v>.26)&(v<.49),navy)
    elif label in ('Backpack','Bodice','Jacket','PleatedSkirt') or label.endswith(('ShortLeg','Sleeve','Pocket')):
        stripe(v>.94,ink)
        stripe((v>.90)&(v<.923),accent)
        if label=='Backpack':
            stripe((v>.24)&(v<.30),gold);stripe((v>.66)&(v<.73),ink)
        if label=='Jacket' or label.endswith('Sleeve'):
            stripe((v>.07)&(v<.13),navy)
    if label=='TrainingWaist':
        fabric[:]=navy
        stripe(v<.15,ink);stripe((v>.12)&(v<.16),accent)
    if style=='ribbon':
        stripe((np.abs(np.sin(angle+.7*v))>.69)&(v>.25),bone if name=='amemori_sumiha' else accent*.70)
        stripe((np.abs(np.sin(angle+.7*v))>.91)&(v>.25),gold if name=='amemori_sumiha' else accent*.63)
    if style=='skin':
        return fabric
    if style in ('sport','ear','lining','ribbon','metal','headset','teal') or label.endswith('Sock'):
        return fabric
    if label.startswith('HairLock'):return fabric
    # Front/back drawings are used as embroidered panels. A continuous hand-colored
    # side panel spans both tangencies, so neither flank samples an occluded view.
    front_mask=1-fade((a-np.deg2rad(42))/np.deg2rad(34))
    rear_mask=fade((a-np.deg2rad(105))/np.deg2rad(40))
    if axis=='horizontal':
        # Tangencies on an arm are its upper and lower surfaces.
        front_mask=1-fade((a-.84)/.64);rear_mask=fade((a-1.70)/.66)
    if is_hair:
        # Ignore ribbon, skin and hat pixels inside hair regions of the source drawing.
        for art in (front,back):
            dist=np.linalg.norm(art-hair,axis=2)
            art[:]=art*(1-fade((dist-63)/50))[...,None]+fabric*fade((dist-63)/50)[...,None]
    if name=='yeh_chengxi' and (is_hair or face):
        crown=fade((32-z)/6)[...,None]
        front=front*(1-crown)+hair*crown
        back=back*(1-crown)+hair*crown
    if face:
        rear_mask=0*rear_mask
        front_mask=1-fade((a-.81)/.48)
    if style=='underpack':rear_mask=rear_mask*0
    if style=='bag':front_mask=front_mask*0
    return fabric+(front-fabric)*front_mask[...,None]+(back-fabric)*rear_mask[...,None]

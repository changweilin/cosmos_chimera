"""Build individually closed lofts in the turnaround's 440-unit drawing space."""
from pathlib import Path
import cv2
from wardrobe_paint import paint
from profile_paint import PROFILES, sample_profile, panels
import numpy as np
from scipy.interpolate import PchipInterpolator

ROOT=Path(__file__).resolve().parent
CACHE=ROOT/'work'
TAU=2*np.pi

def smooth(x):
    x=np.clip(x,0,1)
    return x*x*(3-2*x)

class Figure:
    def __init__(self,name):
        self.name=name
        self.parts=[]
        self.images=[cv2.imread(str(CACHE/(name+s+'.png'))) for s in ('front','back','profile')]
        # The reference's side-facing fist occludes the center of the sleeve opening.
        # Paint lining behind the real modeled hand instead of duplicating the fist.
        self.cuff_plate=self.images[2].copy()
        center={'roen':(54,109),'yeh_chengxi':(53,110),'amemori_sumiha':(42,119)}[name]
        cv2.ellipse(self.cuff_plate,(center[0]*3,center[1]*3),(31,15),0,0,360,(55,49,54),-1,cv2.LINE_AA)
        # The reference's forehead ornament is rebuilt as metal geometry. Inpaint its
        # exact footprint so taking it off reveals uninterrupted bangs.
        if name!='roen':
            source=self.images[0]
            x,z,rx,rz=(-15,50,7.5,7.5) if name=='amemori_sumiha' else (21.5,40,5.4,6)
            mask=np.zeros(source.shape[:2],dtype='uint8')
            cv2.ellipse(mask,(round(source.shape[1]/2+3*x),round(3*z)),(round(rx*3),round(rz*3)),0,0,360,255,-1)
            self.clean_bangs=cv2.inpaint(source,mask,5,cv2.INPAINT_TELEA)
        else:
            self.clean_bangs=self.images[0]

    def part(self,label,stations,axis='vertical',style='cloth',exponent=1):
        # Each station specifies longitudinal position, two center coordinates and two radii.
        s=np.array(stations,float)
        interp=PchipInterpolator(s[:,0],s[:,1:],axis=0)
        def surface(v,a):
            h=s[0,0]+(s[-1,0]-s[0,0])*v
            c,d,r,b=np.moveaxis(interp(h),-1,0)
            sin,cos=np.sin(a),np.cos(a)
            across=np.sign(sin)*np.abs(sin)**exponent
            depth=np.sign(cos)*np.abs(cos)**exponent
            lateral=c+r*across
            y=d-b*depth
            x,z=(lateral,h) if axis=='vertical' else (h,lateral)
            if style=='face':
                chin={'roen':69,'amemori_sumiha':86,'yeh_chengxi':81}[self.name]
                nose=np.exp(-((x-c)/4.4)**2-((z-chin+17)/7.5)**2)
                cheeks=np.exp(-((abs(x-c)-11)/7.8)**2-((z-chin+15)/8)**2)
                y=y-(5*nose+2.2*cheeks)*np.maximum(cos,0)**3
            if style=='skirt':
                y=y+1.5*np.cos(a*13)*np.clip(v,0,1)*cos
                x=x-1.5*np.cos(a*13)*v*sin
            if style=='sleeve':
                z=z+np.sin(a*5)*.9*v
            return np.broadcast_arrays(x,y,z)
        n=80 if style=='face' else 48
        rows=max(7,int((s[-1,0]-s[0,0])/2)+1)
        vs=np.unique(np.round(np.r_[np.linspace(0,1,rows),(s[:,0]-s[0,0])/(s[-1,0]-s[0,0])],8))
        angles=np.arange(n)*TAU/n-np.pi
        x,y,z=surface(vs[:,None],angles[None,:])
        # Scale the full head assembly about the chin, including ears and ornaments.
        if self.name=='roen' and (label in ('Head','Hair') or label.startswith('HairLock') or 'Ear' in label):
            z=70+(z-70)*1.045
        if label=='SailorCap':
            z=z+.33*y
        pos=np.stack((x,y,440-z),axis=-1).reshape(-1,3)
        # Seam vertices are shared in geometry. Only UVs wrap at the back meridian.
        faces=[];uv=[]
        for j in range(len(vs)-1):
            for k in range(n):
                a=j*n+k;b=j*n+(k+1)%n
                faces.append((a,b,b+n,a+n))
                uv.extend([(k/n,1-vs[j]),((k+1)/n,1-vs[j]),((k+1)/n,1-vs[j+1]),(k/n,1-vs[j+1])])
        # A single planar ngon at each end closes the volume without a degenerate pole.
        for j,order in [(0,list(reversed(range(n)))),(len(vs)-1,list(range(n)))]:
            faces.append(tuple(j*n+k for k in order))
            uv.extend([((k+.5)/n,1-vs[j]) for k in order])
        # Texture the full circumference in intrinsic surface coordinates. Use constant
        # angular weights so local folds never introduce islands of rear projection.
        w=max(192,min(1024,int(3*TAU*np.mean(s[:,3:5]))))
        h=max(32,int(3*(s[-1,0]-s[0,0])))
        w=((w+3)//4)*4
        v=(np.arange(h)[:,None]+.5)/h
        angle=((np.arange(w)[None,:]+.5)/w-.5)*TAU
        tx,ty,tz=surface(v,angle)
        def sample(img,coord):
            return cv2.remap(img,np.float32(coord*3+img.shape[1]/2-.5),np.float32(tz*3-.5),cv2.INTER_LINEAR,borderMode=cv2.BORDER_REPLICATE).astype('f')
        # Pull tangent samples away from ink outlines and background on the outermost texels.
        # Sampling the same silhouette line around a curved cheek or calf makes a dark stripe.
        inward=(1-.10*smooth((np.abs(np.sin(angle))-.68)/.32))
        center=interp(s[0,0]+(s[-1,0]-s[0,0])*v)[:,:,0]
        art_x=center+(tx-center)*inward if axis=='vertical' else tx
        front=sample(self.clean_bangs if style in ('face','hair') else self.images[0],art_x)
        back=sample(self.images[1],art_x)
        color=paint(self.name,label,style,angle,v,tz,front,back,axis)
        part_name=label.split('_')[-1]
        profile=PROFILES[self.name].get(part_name)
        if profile:
            # The profile bounds are calibrated independently for each layer, excluding
            # the sleeve, arm or backpack that occludes its neighbors in the drawing.
            ranges=np.asarray(profile)
            if label=='SashTail':
                profile_z=np.broadcast_to(ranges[0,0]+v*(ranges[-1,0]-ranges[0,0]),tz.shape)
            else:
                profile_z=tz+.33*ty if label=='SailorCap' else tz
            depth=.5-.5*np.sign(np.cos(angle))*np.abs(np.cos(angle))**exponent
            plate=self.cuff_plate if part_name.endswith('Sleeve') else self.images[2]
            side=sample_profile(plate,profile,profile_z,depth)
            back_art=color if style=='underpack' else back
            front_art=color if style=='bag' else front
            if label=='FieldCup':
                # Spun metal has continuous circumferential highlights and rolled rims.
                shade=.87+.12*np.cos(angle*2+.3)
                metal=np.array([147,135,135])*shade[...,None]
                front_art=np.broadcast_to(metal,front.shape).copy()
                front_art*=np.where((v<.15)|(v>.86),1.17,1.0)[...,None]
                back_art=front_art
            if label=='Bedroll':
                bands=(np.abs(tx)>20)&(np.abs(tx)<26)
                front_art=np.where(bands[...,None],np.array([66,54,71]),np.array([165,190,213]))
                back_art=front_art
            if label=='PuzzleCharm':
                front_art=side;back_art=side
            if label=='Hood':
                valley=np.exp(-((v-.68-.14*np.cos(angle))/.04)**2)
                hood=np.array([205,212,225])*(1-.16*valley[...,None])
                front_art=hood;back_art=hood
            if self.name=='roen' and part_name=='ShortLeg':
                # The separately modeled cargo pocket covers this patch. A second
                # drawing on the underlying leg would appear beside it at oblique angles.
                outside=tx-3 if label.startswith('L_') else 3-tx
                patch=smooth((outside-29)/5)*smooth((tz-228)/6)*smooth((278-tz)/6)
                twill=np.array([76,69,74])
                front_art=front_art+(twill-front_art)*patch[...,None]
                back_art=back_art+(twill-back_art)*patch[...,None]
                side=side+(twill-side)*(smooth((tz-228)/6)*smooth((278-tz)/6))[...,None]
            if self.name=='amemori_sumiha' and part_name=='Backpack':
                side=np.where((tz<114)[...,None],np.array([66,54,72]),side)
            color=panels(front_art,side,back_art,angle)
        if label=='SailorCap':
            # The band follows the actual sloping rim; it remains one continuous navy
            # strip even where the orthographic crown and brim overlap in the artwork.
            base=np.array([224,225,234])
            navy=np.array([91,63,54])
            piping=np.array([155,165,212])
            fabric=np.broadcast_to(base,color.shape).copy()
            fabric=np.where((v>.64)[...,None],navy,fabric)
            fabric=np.where(((v>.67)&(v<.705))[...,None],piping,fabric)
            color=fabric*(.97+.028*np.cos(angle)[...,None])
        # Give each end its own planar UV island. Its perimeter matches the tube texels
        # and the radial interior converges to a single color without a polar smear.
        cap=min(w//2-4,max(14,int(np.mean(s[:,3:5])*2)))
        pad=3
        margin=cap+2*pad
        atlas=np.empty((h+2*margin,w,3),dtype=np.uint8)
        atlas[margin:margin+h]=np.uint8(np.clip(color,0,255))
        uv=np.asarray(uv,float)
        uv[:,1]=(margin+uv[:,1]*h)/len(atlas)
        yy,xx=np.indices((margin,w))
        for end in (0,1):
            edge=atlas[margin if end==0 else margin+h-1]
            dx=(xx+.5-w/2)/(cap/2)
            dy=(yy+.5-margin/2)/(cap/2)
            radius=np.hypot(dx,dy)
            az=(np.arctan2(dy,dx)/TAU+.5)%1
            image=edge[np.minimum(w-1,(az*w).astype(int))]
            mix=smooth((radius-.18)/.79)[...,None]
            patch=image*mix+np.median(edge,axis=0)*(1-mix)
            if profile and axis=='horizontal':
                # A sleeve points straight at the profile camera. Its end disk has a
                # 2D side drawing; radial extension of the cuff would erase the folds.
                cz,cy,rz,ry=s[0 if end==0 else -1,1:]
                cap_side=sample_profile(plate,profile,cz+rz*dy,.5-.47*dx)
                patch=np.where((radius<.94)[...,None],cap_side,patch)
            at=0 if end==0 else margin+h
            atlas[at:at+margin]=patch.astype(np.uint8)
            atlas[margin-pad:margin] = atlas[margin]
            atlas[margin+h:margin+h+pad] = atlas[margin+h-1]
            order=np.arange(n-1,-1,-1) if end==0 else np.arange(n)
            angle_cap=angles[order]
            start=len(uv)-2*n+end*n
            uv[start:start+n,0]=.5+np.cos(angle_cap)*cap/(2*w)
            uv[start:start+n,1]=1-(at+margin/2+np.sin(angle_cap)*cap/2)/len(atlas)
        file=CACHE/(self.name+'_'+label+'.png')
        cv2.imwrite(str(file),atlas)
        sizes=np.array([len(f) for f in faces],dtype='i')
        self.parts.append((label,pos.astype('f')*(1.65/440),np.concatenate(faces).astype('i'),sizes,np.array(uv,dtype='f')))

    def build(self):
        n=self.name;r=n=='roen';s=n=='amemori_sumiha'
        c=3 if r else 2 if s else 5
        def loft(label,keys,**kw):
            self.part(label,keys,**kw)
        def shape(label,rows,offset=c,**kw):
            loft(label,[(z,offset,y,w,d) for z,w,d,y in rows],**kw)
        chin=70 if r else 89 if s else 83
        shape('Head',[(chin-59,10,13,0),(chin-52,22,24,0),(chin-40,28,28,-1),(chin-27,26,26,-2),(chin-17,23,22,-4),(chin-9,17,17,-5),(chin-3,9,12,-6),(chin,3,6,-6)],style='face')
        shape('Neck',[(chin-10,11,11,1),(chin+3,10,10,1),(chin+15,14,13,1)],style='skin')
        # Hair mass occupies the rear cranium and tapers into the nape.
        crown=4 if r else 12 if s else 17
        shape('Hair',[(crown,4,5,3),(crown+6,17,17,4),(crown+15,27,24,4),(crown+26,32,27,5),(chin-17,30,23,9),(chin-6,23,17,13),(chin+1,12,10,13)],style='hair')
        # Separate tapered locks break up the back of the cranium at oblique angles.
        for k,angle in enumerate(np.linspace(.8,2*np.pi-.8,11)):
            lateral,behind=np.sin(angle),-np.cos(angle)
            z0=crown+(11 if r else 19)+(k%3)*2
            end=(chin-19 if r else chin-5)+(k%4)*2
            loft('HairLock'+str(k+1),[
                (z0,c+19*lateral,5+17*behind,4,4),
                (z0+9,c+25*lateral,5+23*behind,7,6),
                (end-12,c+28*lateral,6+24*behind,6,5),
                (end-5,c+27*lateral,8+24*behind,4,3.5),
                (end,c+(35 if r else 25)*lateral+2,10+(29 if r else 25)*behind,.7,.8)],style='hair')
        for sign,tag in ((-1,'R'),(1,'L')):
            loft('HairLockTemple'+tag,[(chin-48,c+sign*19,-12,3,3),
                 (chin-39,c+sign*23,-15,5,4),(chin-29,c+sign*24,-15,4,4),
                 (chin-20,c+sign*24,-14,3,3),(chin-13,c+sign*20,-13,.8,1)],style='hair')
        if not r:
            if s:
                loft('Topknot',[(1,-6,13,2,2),(8,-6,15,12,10),(18,-8,15,15,13),(30,-7,16,7,7)],style='hair')
            else:
                loft('SailorCap',[(1,3,2,5,6),(5,4,3,16,17),(12,3,3,27,24),(20,3,1,34,27),(26,3,0,34,27),(31,4,0,28,24)])
            off=-31 if s else 47
            loft('TiedHair',[(32,-14 if s else 20,12,4,5),(38,off if s else 36,18,6,6),(48,off,23,12,13),(60,off+(0 if s else 7),27,15,17),(74,off+(0 if s else 5),26,13,13),(87,off,21,8,8),(94,off-4,18,2,2)],style='hair')
        # Model the pinna and inset concha on both sides; the helix protrudes past the hairline.
        for sign,tag in ((-1,'R'),(1,'L')):
            def ear(label,rows,style):
                loft(tag+'_Ear'+label,[(chin+z,c+sign*(24+.65*(x-24)),y+1,w*.73,d*.8) for z,x,y,w,d in rows],style=style)
            ear('Shell',[(-33,25,-6,2,3),(-30,28,-7,4,5),(-23,29,-8,5,6),(-16,27,-7,4,4),(-13,26,-7,2,3)],'ear')
            ear('Concha',[(-29,31,-8,1.5,2),(-24,33,-8,2,3.8),(-19,31,-8,1.3,2)],'lining')
            ear('Ridge',[(-30,31,-9,.8,1.2),(-27,33,-11,1.4,1.7),(-22,33,-11,1.2,1.6),(-19,32,-10,.8,1)],'ear')
        if not r:
            if not s:
                loft('CapPin',[(22,30,-8,1.5,2),(26,32,-8,4,3),(30,30,-8,2,2)],style='metal')
        else:
            for sign,tag in ((-1,'R'),(1,'L')):
                x=c+sign*16
                loft(tag+'_NeckPhone',[(81,x,-15,4,4),(84,x,-17,7,6),(92,x,-18,7,6),(96,x,-17,4,4)],style='headset')
                loft(tag+'_PhoneTrim',[(84,x,-21,3,2),(87,x,-22,5,2),(92,x,-21,3,2)],style='teal')
        shoulder=103 if r else 115 if s else 107
        waist=184 if r else 177 if s else 172
        # Anatomical core extends through the covered areas and shares one skin palette.
        shape('Torso',[(chin+8,10,10,1),(shoulder-9,24,13,1),(shoulder,33,16,1),
                       (shoulder+14,30,18,0),(waist-20,25,16,1),(waist,26,16,1),
                       (waist+15,30,18,2),(waist+24,30,18,2),(waist+34,23,14,2),
                       (waist+41,9,9,2)],style='skin')
        shape('TrainingTop',[(chin+10,13,12,1),(shoulder-8,27,16,1),(shoulder,36,19,1),
                             (shoulder+17,32,21,0),(waist-15,28,19,1),(waist+8,31,20,1)],style='sport')
        shape('TrainingWaist',[(waist+3,29,19,1),(waist+15,33,21,2),
                               (waist+25,32,21,2),(waist+35,27,20,2),(waist+43,14,15,2)],style='sport')
        shape('Jacket' if r else 'Bodice',[(chin+6,14,13,0),(shoulder-14,28,17,0),(shoulder-5,39,20,0),(shoulder+8,36,21,-1),(shoulder+22,32,23,-2),(waist-15,30 if not r else 35,18,-1),(waist,34 if not r else 42,21,0),(waist+8,36 if not r else 45,22,0)],style='underpack')
        if r:
            shape('Hood',[(72,7,6,21),(76,17,9,23),(84,24,12,25),
                          (95,24,13,26),(102,15,9,27),(106,6,5,29)])
        if s:
            shape('PleatedSkirt',[(168,31,19,1),(182,36,22,1),(210,44,25,2),(234,50,28,3),(240,49,27,3)],style='skirt')
        else:
            shape('ShortsWaist',[(waist-3,31,19,0),(waist+5,35,21,1),(waist+19,39,23,2),(waist+30,36,20,2)],style='cloth')
        packtop=102 if r else 94 if s else 106
        shape('Backpack',[(packtop,22,7,20),(packtop+7,32,13,24),(packtop+17,37,16,25),(waist-6,36,17,25),(waist+5,30,13,24),(waist+8,20,7,21)],style='bag',exponent=.57)
        if s:
            loft('Bedroll',[(-42,104,32,9,9),(-39,104,32,11,11),
                            (39,104,32,11,11),(42,104,32,9,9)],axis='horizontal')
            loft('PackTassel',[(169,26,33,1.5,1.5),(179,32,38,1.4,1.4),(185,34,40,2,2),
                               (191,34,40,3,3),(205,35,41,5,5)],style='metal')
        elif r:
            loft('CubeLanyard',[(167,-21,33,1.5,1.5),(172,-32,37,1.4,1.4),(178,-39,39,1.3,1.3),(182,-44,40,1.3,1.3)],style='headset')
            loft('PuzzleCharm',[(179,-43,40,2,2),(182,-45,41,5,5),
                                 (187,-45,41,5,5),(190,-44,40,2.5,2.5)],exponent=.45)
        else:
            loft('CupHanger',[(144,-22,31,2,2),(150,-28,36,2,2),(156,-33,40,2,2),(160,-34,40,2,2)],style='metal')
            loft('FieldCup',[(154,-33,39,6,6),(157,-34,40,10,11),
                             (171,-34,41,10,11),(176,-34,41,9,10),(178,-34,41,6,8)])
        if s:
            loft('SashTail',[(189,37,1,5,4),(215,43,2,8,3),(247,49,2,9,3),(267,47,4,6,2),(278,41,5,1,1)])
        for sign in (-1,1):
            tag='L' if sign>0 else 'R'
            def limb(label,rows,**kw):
                loft(tag+'_'+label,[(z,c+sign*cx,y,rx,ry) for z,cx,y,rx,ry in rows],**kw)
            upper=waist+20
            knee=294 if s else 298 if r else 284
            calf=22 if s or r else 32
            ankle=25 if s else 22 if r else 37
            def leg_axis(z):
                if n=='yeh_chengxi':
                    return 18+19*np.clip((z-upper)/(360-upper),0,1)
                return np.interp(z,[upper,knee,377],[18,20,ankle])
            leg_rows=[(upper,1,12,13),(upper+10,1,15,15),(upper+22,1,16,16),
                      (knee-17,0,13,13),(knee,-1,10,11),(knee+12,0,11,12),
                      (knee+30,1,12,13),(352,1,9,10),(377,1,7,8),(410,1,7,8)]
            limb('Leg',[(z,leg_axis(z),y,rx,ry) for z,y,rx,ry in leg_rows],style='skin')
            limb('Foot',[(399,ankle,1,7,8),(411,ankle,-2,8,12),(422,ankle,-7,10,20),
                         (431,ankle,-8,12,22),(436,ankle,-8,11,20)],style='skin')
            sock=341 if s else 356 if r else 353
            limb('Sock',[(sock,leg_axis(sock),1,11,12),(sock+3,leg_axis(sock+3),1,11.2,12.2),
                         (377,ankle,1,8.5,9.5),(400,ankle,1,8,9)],style='sock')
            limb('TrainingShort',[(waist+20,17,2,17,20),(waist+30,18,2,19,21),
                                  (waist+47,leg_axis(waist+47),1,20,19),(waist+57,leg_axis(waist+57),1,19,18)],style='sport')
            if sign<0 and not r:
                pouch_z=250 if s else 226
                limb('ThighBand',[(pouch_z,20,1,16,16),(pouch_z+3,20,1,17,17),(pouch_z+7,20,1,16,16)])
                limb('ThighPouch',[(pouch_z-1,32,-8,5,4),(pouch_z+3,33,-9,8,6),(pouch_z+20,33,-8,8,6),(pouch_z+23,33,-8,6,5)],exponent=.5)
            if not s:
                hem=293 if r else 236
                limb('ShortLeg',[(waist+19,18,1,17,19),(waist+30,19,1,19,21),(hem-10,22,2,22 if r else 23,22),(hem,22,2,21 if r else 23,21)],exponent=.82)
            boot=387 if r else 375 if s else 352
            limb('Boot',[(boot,ankle,2,12,13),(boot+6,ankle,2,15,15),(400,ankle,1,12,14),(411,ankle,-3,13,20),(423,ankle,-7,16,28),(433,ankle,-8,17,30),(438,ankle,-8,16,29)],exponent=.84)
            if r:
                limb('CargoPocket',[(232,40,-1,4,12),(237,42,-1,5,17),(253,44,-1,5,18),
                                    (269,44,-1,5,17),(274,42,-1,4,13)],exponent=.50)
            def arm(label,rows,**kw):
                stations=[(sign*along,z,y,rz,ry) for along,z,y,rz,ry in rows]
                loft(tag+'_'+label,sorted(stations),axis='horizontal',**kw)
            elbow=82 if r else 88 if s else 78
            hand=126 if s else 118 if r else 114
            arm('Arm',[(23,shoulder,0,10,10),(46,shoulder,0,12,12),(elbow-10,shoulder,0,8,9),(elbow,shoulder,0,7,8),(elbow+7,shoulder,0,8,8),(hand+1,shoulder,-1,3.8,4.8)],style='skin')
            arm('Palm',[(hand-1,shoulder,-1,3.6,4.5),(hand+7,shoulder,0,4,6),(hand+15,shoulder+1,0,2.8,5.3)],style='skin')
            arm('Cuff',[(hand-9,shoulder,0,6,7),(hand-5,shoulder,0,7,7),(hand,shoulder,0,6,6)])
            arm('Glove',[(hand-1,shoulder,0,4,5),(hand+6,shoulder,0,5,7),(hand+13,shoulder+1,0,4,7),(hand+16,shoulder+1,0,3,5)])
            for k,extra in enumerate((8,13,11,7)):
                y=-4.8+k*3.2
                arm('Finger'+str(k+1),[(hand+12,shoulder+1,y,2.1,1.9),(hand+18,shoulder+1.5,y,1.8,1.6),(hand+18+extra,shoulder+2,y+.6,1,1)],style='skin')
            arm('Thumb',[(hand+2,shoulder+1,-4,2.8,3),(hand+7,shoulder+3,-7,2.1,2.4),(hand+13,shoulder+4,-10,1,1.4)],style='skin')
            if s:
                limb('KimonoSleeve',[(105,72,0,23,10),(113,73,0,24,13),(125,74,0,24,12),(156,75,0,21,10),(184,77,1,17,8),(205,82,2,11,5),(218,87,2,4,3)],exponent=.76)
                arm('ShoulderSleeve',[(27,116,0,12,15),(39,117,0,14,16),(56,117,0,14,16),(65,117,0,13,14)])
            else:
                arm('RolledSleeve',[(27,shoulder,0,12,15),(40,shoulder,0,14,17),(54,shoulder+1,0,13,15),(elbow-10,shoulder+1,0,15,16),(elbow-5,shoulder+1,0,15,16),(elbow+1,shoulder,0,12,13)],style='sleeve')
        arrays={}
        for i,(label,*values) in enumerate(self.parts):
            for k,a in zip(('pos','indices','sizes','uv'),values):arrays[str(i)+k]=a
        arrays['labels']=np.array([part[0] for part in self.parts])
        np.savez_compressed(CACHE/(n+'-parts.npz'),**arrays)

if __name__=='__main__':
    for character in ('amemori_sumiha','roen','yeh_chengxi'):
        Figure(character).build()

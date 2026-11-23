"""Build individually closed lofts in the turnaround's 440-unit drawing space."""
from pathlib import Path
import cv2
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
        mask=np.load(CACHE/(name+'-masks.npz'))['side'].astype(bool)
        width=mask.shape[1]
        self.side_bounds=np.stack((mask.argmax(1),width-1-mask[:,::-1].argmax(1)))
        self.side_origin={'roen':48,'amemori_sumiha':43,'yeh_chengxi':47}[name]

    def part(self,label,stations,axis='vertical',style='cloth',exponent=1,side_art=False):
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
        front=sample(self.images[0],art_x)
        back=sample(self.images[1],art_x)
        # A backpack conceals the back of the jacket in the reference. Continue
        # nearby cloth underneath it instead of printing a second backpack on the body.
        if style=='underpack':
            back=front*.70
        if style=='bag':
            front=back*.74
        if style=='skin':
            front[:]=[170,198,239];back=front.copy()
        if label.startswith('HairLock'):
            rgb={'roen':[101,129,158],'amemori_sumiha':[64,42,63],'yeh_chengxi':[64,52,60]}[self.name]
            shade=.91+.13*np.cos(angle)+.045*np.cos(angle*5+.8*v)
            front=shade[...,None]*np.array(rgb);back=front.copy()
        if label.endswith('_Leg'):
            # The opposite calf is partially hidden in the reverse drawing. Its outline
            # must not become a longitudinal black stripe on the exposed skin.
            top={'roen':295,'amemori_sumiha':244,'yeh_chengxi':241}[self.name]
            bottom={'roen':356,'amemori_sumiha':342,'yeh_chengxi':351}[self.name]
            exposed=smooth((tz-top)/4)*smooth((bottom-tz)/4)
            skin=np.array([173,201,242])
            front=front*(1-exposed[...,None]*.72)+skin*exposed[...,None]*.72
            back=back*(1-exposed[...,None])+skin*exposed[...,None]
        if label=='SailorCap':
            band=smooth((19-tz)/4)[...,None]
            back=back*(1-band)+np.array([213,217,230])*band
        if style=='hair' and not label.startswith('HairLock'):
            base={'roen':[104,127,152],'amemori_sumiha':[64,43,64],'yeh_chengxi':[65,52,63]}[self.name]
            back=.4*back+.6*np.array(base)
        az=np.abs(angle)
        blend=smooth((az-np.deg2rad(57))/np.deg2rad(67))[...,None]
        color=front*(1-blend)+back*blend
        if side_art:
            if side_art=='outline':
                row=np.clip((tz*3).astype(int),0,self.side_bounds.shape[1]-1)
                left,right=self.side_bounds[:,row]
                # The model's longitudinal section is matched to the visible side silhouette.
                t=.5-.46*np.sign(np.cos(angle))*abs(np.cos(angle))**exponent
                coord=(left+(right-left)*t)/3
            else:
                coord=ty+self.side_origin
            profile=sample(self.images[2],coord-self.images[2].shape[1]/6)
            side=smooth((az-np.deg2rad(52))/np.deg2rad(30))*(1-smooth((az-np.deg2rad(100))/np.deg2rad(30)))
            color=color*(1-side[...,None])+profile*side[...,None]
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
        shape('Head',[(chin-59,10,13,0),(chin-52,22,24,0),(chin-40,28,28,-1),(chin-27,26,26,-2),(chin-17,23,22,-4),(chin-9,17,17,-5),(chin-3,9,12,-6),(chin,3,6,-6)],style='face',side_art='outline')
        shape('Neck',[(chin-10,11,11,1),(chin+3,10,10,1),(chin+15,14,13,1)],style='skin')
        # Hair mass occupies the rear cranium and tapers into the nape.
        crown=4 if r else 12 if s else 17
        shape('Hair',[(crown,4,5,3),(crown+6,17,17,4),(crown+15,27,24,4),(crown+26,32,27,5),(chin-17,30,23,9),(chin-6,23,17,13),(chin+1,12,10,13)],style='hair',side_art='outline')
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
        if not r:
            if s:
                loft('Topknot',[(1,-6,13,2,2),(8,-6,15,12,10),(18,-8,15,15,13),(30,-7,16,7,7)],style='hair')
                loft('SideRibbon',[(19,-30,1,3,4),(32,-34,0,9,8),(46,-39,0,12,8),(52,-42,1,7,5),(63,-43,2,3,3)])
            else:
                loft('SailorCap',[(1,3,2,5,6),(5,4,3,16,17),(12,3,3,27,24),(20,3,1,34,27),(26,3,0,34,27),(31,4,0,28,24)],side_art='outline')
            off=-31 if s else 47
            loft('TiedHair',[(38,off,18,4,5),(48,off,23,12,13),(60,off+(0 if s else 7),27,15,17),(74,off+(0 if s else 5),26,13,13),(87,off,21,8,8),(94,off-4,18,2,2)],style='hair')
        shoulder=103 if r else 115 if s else 107
        waist=184 if r else 177 if s else 172
        shape('Jacket' if r else 'Bodice',[(chin+6,14,13,0),(shoulder-14,28,17,0),(shoulder-5,39,20,0),(shoulder+8,36,21,-1),(shoulder+22,32,23,-2),(waist-15,30 if not r else 35,18,-1),(waist,34 if not r else 42,21,0),(waist+8,36 if not r else 45,22,0)],style='underpack')
        if s:
            shape('PleatedSkirt',[(168,31,19,1),(182,36,22,1),(210,44,25,2),(234,50,28,3),(240,49,27,3)],style='skirt')
        else:
            shape('ShortsWaist',[(waist-3,31,19,0),(waist+5,35,21,1),(waist+19,39,23,2),(waist+30,36,20,2)],style='cloth')
        packtop=102 if r else 94 if s else 106
        shape('Backpack',[(packtop,22,7,20),(packtop+7,32,13,24),(packtop+17,37,16,25),(waist-6,36,17,25),(waist+5,30,13,24),(waist+8,20,7,21)],style='bag',exponent=.57,side_art=True)
        if s:
            loft('SashTail',[(189,37,1,5,4),(215,43,2,8,3),(247,49,2,9,3),(267,47,4,6,2),(278,41,5,1,1)])
        for sign in (-1,1):
            tag='L' if sign>0 else 'R'
            def limb(label,rows,**kw):
                loft(tag+'_'+label,[(z,c+sign*cx,y,rx,ry) for z,cx,y,rx,ry in rows],**kw)
            upper=221 if s else 207
            knee=294 if s else 298 if r else 284
            calf=22 if s or r else 32
            ankle=25 if s else 22 if r else 37
            limb('Leg',[(upper,19,1,15,16),(upper+19,20,1,17,17),(knee-17,20,0,13,13),(knee,19,-2,10,12),(knee+12,calf,1,11,12),(knee+30,calf+2,3,12,14),(352,ankle,3,10,11),(377,ankle,2,8,8),(398,ankle,2,8,9)],side_art='outline')
            if sign<0 and not r:
                pouch_z=250 if s else 226
                limb('ThighBand',[(pouch_z,20,1,16,16),(pouch_z+3,20,1,17,17),(pouch_z+7,20,1,16,16)])
                limb('ThighPouch',[(pouch_z-1,32,-8,5,4),(pouch_z+3,33,-9,8,6),(pouch_z+20,33,-8,8,6),(pouch_z+23,33,-8,6,5)],exponent=.5)
            if not s:
                hem=293 if r else 236
                limb('ShortLeg',[(waist+19,18,1,17,19),(waist+30,19,1,19,21),(hem-10,22,2,22 if r else 23,22),(hem,22,2,21 if r else 23,21)],exponent=.82)
            boot=387 if r else 375 if s else 352
            limb('Boot',[(boot,ankle,2,12,13),(boot+6,ankle,2,15,15),(400,ankle,1,12,14),(411,ankle,-3,13,20),(423,ankle,-7,16,28),(433,ankle,-8,17,30),(438,ankle,-8,16,29)],exponent=.84,side_art='outline')
            if r:
                limb('CargoPocket',[(231,35,-10,8,4),(237,37,-12,10,4),(254,39,-13,11,5),(271,40,-11,11,4),(275,41,-10,8,3)],exponent=.52)
            def arm(label,rows,**kw):
                stations=[(sign*along,z,y,rz,ry) for along,z,y,rz,ry in rows]
                loft(tag+'_'+label,sorted(stations),axis='horizontal',**kw)
            elbow=82 if r else 88 if s else 78
            hand=126 if s else 118 if r else 114
            arm('Arm',[(29,shoulder,0,12,13),(46,shoulder,0,12,12),(elbow-10,shoulder,0,8,9),(elbow,shoulder,0,7,8),(elbow+7,shoulder,0,8,8),(hand,shoulder,-1,4,5)])
            arm('Cuff',[(hand-9,shoulder,0,6,7),(hand-5,shoulder,0,7,7),(hand,shoulder,0,6,6)])
            arm('Glove',[(hand-1,shoulder,0,4,5),(hand+6,shoulder,0,5,7),(hand+13,shoulder+1,0,4,7),(hand+16,shoulder+1,0,3,5)])
            for k,extra in enumerate((8,13,11,7)):
                y=-4.8+k*3.2
                arm('Finger'+str(k+1),[(hand+12,shoulder+1,y,2.1,1.9),(hand+18,shoulder+1.5,y,1.8,1.6),(hand+18+extra,shoulder+2,y+.6,1,1)])
            arm('Thumb',[(hand+2,shoulder+1,-4,2.8,3),(hand+7,shoulder+3,-7,2.1,2.4),(hand+13,shoulder+4,-10,1,1.4)])
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

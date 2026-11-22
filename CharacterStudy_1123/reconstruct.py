from pathlib import Path
import cv2
import numpy as np
from scipy import ndimage as ndi
from skimage.measure import marching_cubes

ROOT=Path(__file__).resolve().parent
CACHE=ROOT/'work'
CACHE.mkdir(exist_ok=True)
REF=Path(r'C:/Users/user/Documents/app/cosmos_chimera_BGM/image')
NAMES=['amemori_sumiha','roen','yeh_chengxi']
H,W,D=440,358,120

def subjects(img):
    bg=np.median(img[:15],axis=(0,1))
    diff=np.abs(img.astype(float)-bg).max(axis=2)
    seed=cv2.morphologyEx(np.uint8(diff>10),cv2.MORPH_CLOSE,cv2.getStructuringElement(cv2.MORPH_ELLIPSE,(5,5)))
    _,holes,st,_=cv2.connectedComponentsWithStats(1-seed)
    for k in range(1,len(st)):
        if st[k,4]<1600:seed[holes==k]=1
    n,labels,stats,_=cv2.connectedComponentsWithStats(seed)
    ids=sorted([i for i in range(1,n) if stats[i,4]>5000],key=lambda i:stats[i,0])
    return [(img[y:y+h,x:x+w].copy(),np.uint8(labels[y:y+h,x:x+w]==i)) for i in ids for x,y,w,h,_ in [stats[i]]]

def pad(im,mask,w,h):
    outside=~mask.astype(bool)
    near=ndi.distance_transform_edt(outside,return_distances=False,return_indices=True)
    filled=im[tuple(near)]
    return cv2.resize(filled,(w,h),interpolation=cv2.INTER_CUBIC),cv2.resize(mask,(w,h),interpolation=cv2.INTER_NEAREST)

def extract(name):
    read=lambda kind:cv2.imread(str(next(REF.glob('*'+name+'_'+kind+'*.png'))))
    (front,fm),(side,sm),(back,bm),_=subjects(read('turnaround'))
    large,lm=subjects(read('front'))[0]
    if name=='amemori_sumiha':
        side,sm=side[:,::-1],sm[:,::-1]
    # Common horizontal scale is defined by the turnaround, whose views share a pose.
    fw=round(H*fm.shape[1]/fm.shape[0]); sw=round(H*sm.shape[1]/sm.shape[0])
    big,mask=pad(large,lm,fw*3,H*3)
    side,sm=pad(side,sm,sw*3,H*3)
    back,bm=pad(back[:,::-1],bm[:,::-1],fw*3,H*3)
    front,fm=pad(front,fm,fw*3,H*3)
    # Register the sharper standalone drawing to the front of the turnaround.
    gray=lambda a:cv2.cvtColor(a,cv2.COLOR_BGR2GRAY)
    a=front.copy();a[fm==0]=79
    b=big.copy();b[mask==0]=79
    flow=cv2.DISOpticalFlow_create(cv2.DISOPTICAL_FLOW_PRESET_MEDIUM).calc(gray(a),gray(b),None)
    yy,xx=np.indices(fm.shape,dtype='f')
    fx,fy=xx+flow[:,:,0],yy+flow[:,:,1]
    big=cv2.remap(big,fx,fy,cv2.INTER_LINEAR,borderMode=cv2.BORDER_REPLICATE)
    # Keep the artwork pixels wherever the second drawing changes silhouette.
    err=cv2.GaussianBlur(np.abs(big.astype('f')-front).mean(axis=2),(0,0),3)
    blend=np.clip((43-err)/25,0,1)[:,:,None]
    big=np.uint8(big*blend+front*(1-blend))
    # Align reverse silhouette to the same canonical front coordinates, independently per row.
    remap=xx.copy()
    for j in range(len(xx)):
        l=np.flatnonzero(fm[j]); r=np.flatnonzero(bm[j])
        if len(l)>1 and len(r)>1:
            remap[j]=np.interp(xx[j],[l[0],l[-1]],[r[0],r[-1]])
    back=cv2.remap(back,remap,yy,cv2.INTER_LINEAR,borderMode=cv2.BORDER_REPLICATE)
    for key,img in [('front',big),('profile',side),('back',back)]:
        cv2.imwrite(str(CACHE/(name+key+'.png')),img)
    f=np.pad(cv2.resize(fm,(fw,H),interpolation=cv2.INTER_NEAREST),((2,2),(2,2)))
    s=np.pad(cv2.resize(sm,(sw,H),interpolation=cv2.INTER_NEAREST),((2,2),(2,2)))
    # Reconstruct rounded horizontal sections, keeping the two feet and hanging sleeves separate.
    zlen,xlen=f.shape; dlen=s.shape[1]
    contour=cv2.distanceTransform(f,cv2.DIST_L2,5)-cv2.distanceTransform(1-f,cv2.DIST_L2,5)
    depth=np.arange(dlen)[None,:]; x=np.arange(xlen)
    vol=np.full((zlen,xlen,dlen),-3,dtype='f')
    center_map=np.zeros((zlen,xlen),dtype='f')
    width_map=center_map.copy()
    section_mask=cv2.morphologyEx(f,cv2.MORPH_CLOSE,cv2.getStructuringElement(cv2.MORPH_ELLIPSE,(11,9)))
    for j in range(2,H+2):
        ids=np.flatnonzero(s[j]); height=(j-2)/H
        if not len(ids):continue
        near,far=ids[0],ids[-1]
        cuts=np.flatnonzero(np.diff(np.r_[0,section_mask[j],0]))
        for left,right in cuts.reshape(-1,2):
            xi=x[left:right]; radius=(right-left)/2
            unit=(xi-left+.5)/max(radius,.5)-1
            rounded=np.sqrt(np.maximum(.025,1-unit**2))
            outward=np.abs((xi-xlen/2)/H)
            # Arms and kimono sleeves hang on the shoulder plane, in front of the pack.
            arm=np.clip((outward-.088)/.047,0,1) if .18<height<.54 else np.zeros_like(unit)
            mid=(near+far)/2
            center=mid*(1-arm)+dlen*.49*arm
            half=(far-near+1)/2*rounded
            armrad=np.clip(contour[j,xi]*1.20,1.2,H*(.026 if name=='amemori_sumiha' else .034))
            half=half*(1-arm)+armrad*arm
            # Reserve rear volume for the pack, which is narrower than the shoulders.
            if .23<height<.50:
                center-=np.minimum(6,(outward/.10)**3)*(.7*(1-arm))
            center_map[j,left:right]=center
            width_map[j,left:right]=half
    # Smooth envelope independently of the fine silhouette at hair and cloth edges.
    weight=ndi.gaussian_filter(np.float32(width_map>0),(4,2))
    center_map=ndi.gaussian_filter(center_map,(4,2))/np.maximum(weight,.001)
    width_map=ndi.gaussian_filter(width_map,(4,2))/np.maximum(weight,.001)
    for j in range(zlen):
        left,right=0,xlen
        half=width_map[j];center=center_map[j];xi=x
        section=half[:,None]-np.abs(depth-center[:,None])
        vol[j,left:right]=np.minimum(section,contour[j,left:right,None])
    verts,faces,norms,_=marching_cubes(ndi.gaussian_filter(vol,.68),0,allow_degenerate=False)
    z,x,y=verts.T
    uv=np.stack([(x-1.5)/fw,1-(z-1.5)/H,(y-1.5)/sw],axis=1)
    pos=np.stack([(x-xlen/2)/H,(y-dlen*.5)/H,(H+1.5-z)/H],axis=1)*1.65
    # marching cubes normals point outwards; estimate view weights from a smoother field
    smooth=ndi.gaussian_filter(vol,2.5)
    grad=np.array([ndi.map_coordinates(t,verts.T,order=1) for t in np.gradient(smooth)]).T
    nz,nx,ny=(-grad/(np.linalg.norm(grad,axis=1,keepdims=True)+1e-6)).T
    def ramp(v): 
        t=np.clip(v,0,1);return t*t*(3-2*t)
    local=(y-ndi.map_coordinates(center_map,[z,x],order=1))/np.maximum(1,ndi.map_coordinates(width_map,[z,x],order=1))
    rear=ramp((local+.09)/.18)
    sideweight=ramp((np.abs(nx)-.62*np.abs(ny)-.04)/.55)
    # A profile depicts the central figure. Hands extended away from it use palm/glove artwork.
    sideweight*=1-ramp((np.abs(pos[:,0])/1.65-.08)/.052)
    np.savez_compressed(CACHE/(name+'.npz'),pos=pos,faces=faces[:,::-1],uv=uv,weights=np.stack((rear,sideweight),axis=1))
    print(name,len(verts),len(faces),flush=True)
for name in NAMES:extract(name)

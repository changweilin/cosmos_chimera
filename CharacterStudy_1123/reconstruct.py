from pathlib import Path
import cv2
import numpy as np
from scipy import ndimage as ndi


ROOT=Path(__file__).resolve().parent
CACHE=ROOT/'work'
CACHE.mkdir(exist_ok=True)
REF=Path(r'C:/Users/user/Documents/app/cosmos_chimera_BGM/image')
NAMES=['amemori_sumiha','roen','yeh_chengxi']
H=440

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
    if name=='roen':
        # Charcoal fabric is within the segmentation tolerance of the studio backdrop.
        # Keep the original pixels inside the jacket and shorts before edge dilation.
        for silhouette in (fm,lm,bm):
            h,w=silhouette.shape
            for row in range(int(.26*h),int(.66*h)):
                occupied=np.flatnonzero(silhouette[row])
                if occupied.size:
                    lo=max(occupied[0],round(w*.395))
                    hi=min(occupied[-1],round(w*.605))
                    silhouette[row,lo:hi+1]=1
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
    np.savez_compressed(CACHE/(name+'-masks.npz'),front=fm,side=sm,back=bm)
    for key,img in [('front',big),('profile',side),('back',back)]:
        cv2.imwrite(str(CACHE/(name+key+'.png')),img)
if __name__ == '__main__':
    for name in NAMES: extract(name)

"""Landmarks in the supplied, left-facing 440-unit profile plates.
Each row describes image height and the visible front/rear edges of ONE part.
"""
import cv2
import numpy as np

PROFILES={
    'amemori_sumiha':{
        'Bedroll':[(94,69,75),(104,61,83),(114,69,75)],
        'Bodice':[(95,32,49),(117,20,57),(151,12,54),(180,13,49)],
        'Backpack':[(100,60,79),(120,58,86),(145,57,92),(177,57,96),(194,60,87)],
        'PleatedSkirt':[(167,20,54),(193,15,60),(219,7,69),(239,6,72)],
        'KimonoSleeve':[(106,34,47),(121,29,56),(148,29,55),(182,34,53),(208,39,49),(220,42,45)],
        'ShoulderSleeve':[(101,36,46),(117,28,55),(130,31,53)],
        'Boot':[(375,42,65),(387,42,67),(402,33,67),(419,13,71),(431,10,74),(437,10,74)],
        'ThighBand':[(248,24,58),(258,25,59)],'ThighPouch':[(251,29,49),(276,32,48)],
        'SashTail':[(193,46,59),(210,56,70),(224,55,66)],
    },
    'roen':{
        'PuzzleCharm':[(178,94,98),(183,90,102),(189,94,98)],
        'Jacket':[(78,46,66),(100,31,69),(126,24,67),(154,20,67),(184,17,67),(191,16,67)],
        'Hood':[(72,58,80),(80,48,84),(91,53,86),(104,67,85)],
        'Backpack':[(108,73,85),(118,73,94),(150,73,98),(179,72,98),(190,73,94)],
        'ShortsWaist':[(183,23,65),(196,24,68),(217,23,69)],
        'ShortLeg':[(203,23,68),(239,22,73),(269,22,76),(292,22,77)],
        'CargoPocket':[(234,32,66),(242,31,69),(270,33,68),(275,33,65)],
        'RolledSleeve':[(87,45,60),(97,40,67),(112,40,68),(122,46,61)],
        'Boot':[(387,46,66),(397,39,68),(408,28,70),(420,10,72),(431,3,74),(438,3,74)],
        'NeckPhone':[(79,29,35),(86,25,39),(91,25,38),(95,29,35)],
    },
    'yeh_chengxi':{
        'FieldCup':[(155,93,101),(158,86,105),(170,86,106),(177,90,102)],
        'Bodice':[(85,39,53),(105,24,62),(129,17,64),(154,19,63),(172,20,63)],
        'Backpack':[(111,66,77),(128,66,90),(155,67,98),(178,67,99),(186,68,84)],
        'ShortsWaist':[(169,19,60),(180,16,61),(201,15,64)],
        'ShortLeg':[(192,15,63),(218,18,66),(232,18,66)],
        'RolledSleeve':[(92,45,59),(104,38,67),(117,38,67),(123,43,61)],
        'Boot':[(352,34,65),(365,34,69),(380,33,69),(403,29,68),(419,9,69),(435,3,72)],
        'SailorCap':[(2,41,56),(10,37,72),(19,34,84),(27,38,90),(38,58,93),(43,75,89)],
    },
}

def sample_profile(image,landmarks,z,depth_fraction):
    rows=np.asarray(landmarks)
    near=np.interp(z,rows[:,0],rows[:,1])
    far=np.interp(z,rows[:,0],rows[:,2])
    px=near+(far-near)*np.clip(depth_fraction,.025,.975)
    return cv2.remap(image,np.float32(3*px-.5),np.float32(3*z-.5),cv2.INTER_LINEAR,borderMode=cv2.BORDER_REPLICATE).astype('f')

def stitch(first,second):
    """Cut through agreeing colors in each overlap, preserving line art on either side."""
    h,w=first.shape[:2]
    delta=cv2.GaussianBlur(np.abs(first-second).mean(axis=2),(0,0),1.2)
    delta+=8*np.square(np.linspace(-1,1,w))[None,:]
    cost=delta[0].copy()
    paths=np.zeros((h,w),dtype=np.int8)
    for j in range(1,h):
        choices=np.stack((np.r_[np.inf,cost[:-1]]+2,cost,np.r_[cost[1:],np.inf]+2))
        choice=choices.argmin(axis=0)
        paths[j]=choice-1
        cost=delta[j]+choices.min(axis=0)
    boundary=np.zeros(h,dtype=int)
    boundary[-1]=cost.argmin()
    for j in range(h-1,0,-1):
        boundary[j-1]=boundary[j]+paths[j,boundary[j]]
    boundary=cv2.GaussianBlur(boundary.astype('f')[:,None],(1,9),2)
    alpha=np.clip((np.arange(w)[None,:]-boundary)/max(5,w*.22)+.5,0,1)
    alpha[:,0]=0;alpha[:,-1]=1
    low1=cv2.GaussianBlur(first,(0,0),2)
    low2=cv2.GaussianBlur(second,(0,0),2)
    coarse=cv2.GaussianBlur(alpha,(0,0),2)
    coarse[:,0]=0;coarse[:,-1]=1
    return low1+(low2-low1)*coarse[...,None]+(first-low1)*(1-alpha[...,None])+(second-low2)*alpha[...,None]

def panels(front,side,back,angle):
    front,side,back=(np.asarray(im,dtype=np.float32) for im in (front,side,back))
    result=front.copy()
    for direction in (-1,1):
        ix=np.flatnonzero(angle.ravel()*direction>0)
        ix=ix[np.argsort(abs(angle.ravel()[ix]))]
        degrees=np.rad2deg(abs(angle.ravel()[ix]))
        for lower,upper,a,b in [(41,68,front,side),(116,143,side,back)]:
            ids=ix[(degrees>=lower)&(degrees<=upper)]
            result[:,ids]=stitch(a[:,ids],b[:,ids])
        ids=ix[(degrees>68)&(degrees<116)]
        result[:,ids]=side[:,ids]
        ids=ix[degrees>143]
        result[:,ids]=back[:,ids]
    return result

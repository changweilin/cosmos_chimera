from pathlib import Path
import bpy,bmesh
from mathutils import Vector
p=Path(__file__).resolve().parent
ratios={}
for f in sorted(p.glob('*.blend')):
    bpy.ops.wm.open_mainfile(filepath=str(f))
    objects=[o for o in bpy.data.objects if o.type=='MESH']
    bad=flat=0
    for o in objects:
        bm=bmesh.new();bm.from_mesh(o.data)
        bad+=sum(not e.is_manifold for e in bm.edges)
        flat+=sum(face.calc_area()<1e-14 for face in bm.faces)
        assert bm.calc_volume(signed=True)>0,o.name
        euler=len(bm.verts)-len(bm.edges)+len(bm.faces)
        if o.name!='Body surface':
            assert euler==(0 if o.name in ('Triangle hair clip','Bow upper loop','Bow lower loop') else 2),(o.name,euler)
        bm.free()
    print('CHECK',f.name,len(objects),'nonmanifold',bad,'degenerate',flat)
    assert bad==flat==0
    assert all(im.packed_file for im in bpy.data.images if im.source=='FILE')
    ctl=bpy.data.objects['Wardrobe']
    base=set(bpy.data.collections['Body'].objects)|set(bpy.data.collections['Hair'].objects)
    sets=[base,base|set(bpy.data.collections['01 Reference'].all_objects),
          base|set(bpy.data.collections['02 Training'].all_objects)]
    assert ctl['outfit']==1
    for i in (2,0,1):
        ctl['outfit']=i;ctl.update_tag();bpy.context.view_layer.update()
        assert {o for o in objects if not o.hide_render}==sets[i]
        assert {o for o in objects if not o.hide_viewport}==sets[i]
    def limits(objs):
        zs=[p[2] for o in objs for p in o.bound_box]
        return min(zs),max(zs)
    # The landmarks must be anatomical. The hat, bun and tuft have unrelated heights.
    head=bpy.data.objects['Head']
    sole,_=limits([bpy.data.objects['Body surface']])
    chin,crown=limits([head])
    ratios[f.stem]=((crown-sole)/(crown-chin),head.dimensions.x/(crown-sole))
    print('Bare head (cm):',round((crown-chin)*100,2),round(head.dimensions.x*100,2))
    if f.stem=='yeh_chengxi':
        surface=bpy.data.objects['Body surface']
        for side in (-1,1):
            centers=[]
            for z in range(245,351,5):
                h=(440-z)*1.65/440
                hits=[x for x in range(1,62) if surface.ray_cast(Vector(((5+side*x)*1.65/440,-1,h)),Vector((0,1,0)))[0]]
                assert hits,z
                centers.append((min(hits)+max(hits))/2)
            slopes=[b-a for a,b in zip(centers,centers[1:])]
            assert min(slopes)>=-.5 and max(slopes)<=1.5,slopes
    if f.stem=='roen':
        rays=0
        for obj,xc,depths in [('Jacket',3,range(120,178,3)),('L_ShortLeg',23,range(216,290,3)),('R_ShortLeg',-17,range(216,290,3))]:
            for z in depths:
                for dx in (-8,0,8):
                    for sign in (-1,1):
                        origin=Vector(((xc+dx)*1.65/440,sign*1.,(440-z)*1.65/440))
                        assert bpy.data.objects[obj].ray_cast(origin,Vector((0,-sign,0)))[0],(obj,xc+dx,z)
                        rays+=1
        print('ROEN opposite-side fabric hits:',rays)

print('Anatomical heads and head-width/stature:',ratios)
for index,tolerance in enumerate((.12,.007)):
    peers=[ratios[name][index] for name in ('amemori_sumiha','yeh_chengxi')]
    assert min(peers)-tolerance<=ratios['roen'][index]<=max(peers)+tolerance

from pathlib import Path
import bpy,bmesh
from mathutils import Vector
p=Path(__file__).resolve().parent
for f in sorted(p.glob('*.blend')):
    bpy.ops.wm.open_mainfile(filepath=str(f))
    objects=[o for o in bpy.data.objects if o.type=='MESH']
    bad=flat=0
    for o in objects:
        bm=bmesh.new();bm.from_mesh(o.data)
        bad+=sum(not e.is_manifold for e in bm.edges)
        flat+=sum(face.calc_area()<1e-14 for face in bm.faces)
        assert bm.calc_volume(signed=True)>0 and len(bm.verts)-len(bm.edges)+len(bm.faces)==2,o.name
        bm.free()
    print('CHECK',f.name,len(objects),'nonmanifold',bad,'degenerate',flat)
    assert bad==flat==0
    assert all(im.packed_file for im in bpy.data.images if im.source=='FILE')
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

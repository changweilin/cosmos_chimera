"""Head ornaments expressed in the same drawing units as the character assembly."""
import bpy
import bmesh
from mathutils import Vector
from math import sin,cos,pi

def ornaments(name,collection,rig):
    scale=1.65/440
    def point(p):
        return Vector((p[0]*scale,p[1]*scale,(440-p[2])*scale))
    def material(key,color):
        m=bpy.data.materials.new(key)
        m.diffuse_color=(*color,1)
        m.use_nodes=True
        b=m.node_tree.nodes.get('Principled BSDF')
        b.inputs['Base Color'].default_value=(*color,1)
        b.inputs['Emission Color'].default_value=(*color,1)
        b.inputs['Emission Strength'].default_value=.38
        b.inputs['Metallic'].default_value=.38
        b.inputs['Roughness'].default_value=.34
        return m
    if name=='roen':return []
    gold=material('Brushed brass',(.90,.54,.14))

    result=[]
    def add(label,verts,faces,mat):
        mesh=bpy.data.meshes.new(label)
        bpy.context.view_layer.update()
        aligned=[]
        for x,y,z in verts:
            ray=point((x,-100,z))
            hits=[obj.ray_cast(ray,Vector((0,1,0))) for obj in (bpy.data.objects['Head'],bpy.data.objects['Hair'])]
            depths=[hit[1].y for hit in hits if hit[0]]
            vertex=point((x,y,z))
            if depths:
                # Preserve the relief of the backing and ridge relative to the local scalp.
                relief=(y+29+(x+16)*.36) if name=='amemori_sumiha' else (y+25-.65*(x-22))
                vertex.y=min(depths)+(relief-.7)*scale
            aligned.append(vertex)
        mesh.from_pydata(aligned,[],faces)
        bm=bmesh.new();bm.from_mesh(mesh)
        bmesh.ops.recalc_face_normals(bm,faces=list(bm.faces))
        bm.to_mesh(mesh);bm.free()
        obj=bpy.data.objects.new(label,mesh);collection.objects.link(obj)
        obj.parent=rig;mesh.materials.append(mat);result.append(obj)
        for f in mesh.polygons:f.use_smooth=True
    if name=='amemori_sumiha':
        # Hollow triangular hair clip, with independent inner and outer walls.
        outlines=[(-21,46),(-10,50),(-18,57)]
        verts=[]
        for y,r in [(-29,1),(-31,1),(-31,.58),(-29,.58)]:
            for x,z in outlines:
                xx=-16+(x+16)*r
                verts.append((xx,y-(xx+16)*.36,51+(z-51)*r))
        faces=[(3*i+j,3*i+(j+1)%3,3*((i+1)%4)+(j+1)%3,3*((i+1)%4)+j) for i in range(4) for j in range(3)]
        add('Triangle hair clip',verts,faces,gold)
    else:
        # A faceted four-point star has a raised ridge and a closed backing.
        verts=[(22,-28,40),(22,-23,40)]
        for j in range(8):
            a=pi*j/4;rad=4.8 if j%2==0 else 2.1
            dx=rad*cos(a);verts.append((22+dx,-25+.65*dx,40+rad*sin(a)))
        faces=[f for i in range(8) for f in ((0,2+i,2+(i+1)%8),(1,2+(i+1)%8,2+i))]
        add('North star hair pin',verts,faces,gold)
    return result

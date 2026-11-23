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
    # The two actual ribbon loops and the tail carry woven borders around both
    # faces. The same stripes follow their folds in front, profile and rear views.
    orange=material('Vermilion silk',(.66,.125,.052))
    ivory=material('Ivory silk',(.91,.73,.60))
    shadow=material('Ribbon lining',(.41,.075,.033))
    sumi=name=='amemori_sumiha'
    sign=-1 if sumi else 1
    lateral=Vector((sign,0,0))
    knot=Vector((0,20,43) if sumi else (0,23,52))
    bpy.context.view_layer.update()
    ponytail=bpy.data.objects['TiedHair']
    ray=point((sign*100,knot.y,knot.z))
    hit,surface,_,_=ponytail.ray_cast(ray,-lateral)
    if not hit:
        raise ValueError(f'{name}: no ponytail surface at the tie')
    knot.x=surface.x/scale-sign*1.5
    def ribbon(label,end,width,loop):
        delta=Vector(end)
        across=Vector((0,-delta.z,delta.y)).normalized()
        vertices=[];faces=[];slots=[]
        count=48 if loop else 24
        # Each section has an outer and an inner layer with three continuous fabric bands.
        fractions=(-1,-.76,.76,1)
        for j in range(count):
            t=j/count if loop else j/(count-1)
            theta=2*pi*t
            u=(1-cos(theta))/2 if loop else t
            center=knot+delta*u+lateral*(3.3*sin(theta) if loop else 2*sin(pi*t))
            tangent=delta*(.5*sin(theta))+lateral*(3.3*cos(theta)) if loop else delta+lateral*(2*pi*cos(pi*t))
            normal=tangent.cross(across).normalized()
            half=1.5+width*(u**.65)
            for layer in (-1,1):
                for f in fractions:
                    q=center+across*(f*half)+normal*(.49*layer+.85*(1-abs(f))*u)
                    vertices.append(point(q))
        for j in range(count if loop else count-1):
            k=(j+1)%count
            for layer in range(2):
                for band in range(3):
                    a=8*j+4*layer+band;b=8*k+4*layer+band
                    faces.append((a,a+1,b+1,b))
                    slots.append(1 if band==1 and sumi and label!='Bow lower loop' else (2 if layer==0 else 0))
            for edge in (0,3):
                a=8*j+edge;b=8*k+edge
                faces.append((a,b,b+4,a+4));slots.append(0)
        if not loop:
            for j in (0,count-1):
                for band in range(3):
                    a=8*j+band;faces.append((a,a+1,a+5,a+4));slots.append(0)
        mesh=bpy.data.meshes.new(label);mesh.from_pydata(vertices,[],faces)
        for mat in (orange,ivory,shadow):mesh.materials.append(mat)
        for poly,slot in zip(mesh.polygons,slots):poly.material_index=slot;poly.use_smooth=True
        bm=bmesh.new();bm.from_mesh(mesh);bmesh.ops.recalc_face_normals(bm,faces=list(bm.faces))
        bm.to_mesh(mesh);bm.free()
        obj=bpy.data.objects.new(label,mesh);collection.objects.link(obj);obj.parent=rig;result.append(obj)
    ribbon('Bow upper loop',(sign*7,22 if sumi else 6,-16),7 if sumi else 3.8,True)
    ribbon('Bow lower loop',(sign*9,24,11 if sumi else 6),7 if sumi else 4,True)
    ribbon('Bow tail',(sign*6,6,33 if sumi else 13),5 if sumi else 2.6,False)
    # Side-facing metalwork anchors the loops to the gathered hair.
    def bead(label,center,radii,mat):
        bm=bmesh.new()
        bmesh.ops.create_uvsphere(bm,u_segments=24,v_segments=12,radius=1)
        for vert in bm.verts:
            q=Vector([vert.co[i]*radii[i]+center[i] for i in range(3)])
            vert.co=point(q)
        bmesh.ops.recalc_face_normals(bm,faces=list(bm.faces))
        mesh=bpy.data.meshes.new(label);bm.to_mesh(mesh);bm.free()
        obj=bpy.data.objects.new(label,mesh);collection.objects.link(obj);obj.parent=rig
        mesh.materials.append(mat);result.append(obj)
        for poly in mesh.polygons:poly.use_smooth=True
    if sumi:
        for i in range(4):
            a=pi/4+i*pi/2
            bead('Bow flower petal '+str(i+1),knot+lateral*3+Vector((0,3.2*cos(a),3.2*sin(a))),(2.4,3.4,3.4),gold)
    bead('Bow knot',knot+lateral*1.6,(3,3.2,3.2),gold if not sumi else shadow)
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

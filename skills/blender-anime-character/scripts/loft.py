"""Build a closed editable Z-axis loft with shared seam vertices and per-corner UVs."""
from math import cos, pi, sin
import bmesh
import bpy


def loft(name, rings, collection, material, segments=48):
    """rings = ascending (z, center_x, center_y, radius_x, radius_y) in Blender units."""
    rings=list(rings)
    if len(rings)<2 or segments<3 or any(min(r[3:])<=0 for r in rings):
        raise ValueError('Provide two or more rings with positive radii and at least three sides')
    if any(a[0]>=b[0] for a,b in zip(rings,rings[1:])):
        raise ValueError('Ring heights must increase')
    vertices=[]
    for z,x,y,rx,ry in rings:
        for j in range(segments):
            t=2*pi*j/segments
            vertices.append((x+rx*sin(t),y+ry*cos(t),z))
    faces=[]; coords=[]
    def band(z):
        return .24+.52*(z-rings[0][0])/(rings[-1][0]-rings[0][0])
    for i in range(len(rings)-1):
        for j in range(segments):
            a=i*segments+j;b=i*segments+(j+1)%segments
            faces.append((a,b,b+segments,a+segments))
            u=j/segments;u1=(j+1)/segments
            low,high=band(rings[i][0]),band(rings[i+1][0])
            coords.append(((u,low),(u1,low),(u1,high),(u,high)))
    for i,center_v in ((0,.12),(len(rings)-1,.88)):
        order=list(range(segments)) if i else list(reversed(range(segments)))
        faces.append(tuple(i*segments+j for j in order))
        coords.append(tuple((.5+.11*sin(2*pi*j/segments),center_v+.11*cos(2*pi*j/segments)) for j in order))
    mesh=bpy.data.meshes.new(name)
    mesh.from_pydata(vertices,[],faces)
    uv=mesh.uv_layers.new(name='Wrap')
    for poly,chart in zip(mesh.polygons,coords):
        poly.use_smooth=len(chart)==4
        for idx,value in zip(poly.loop_indices,chart):
            uv.data[idx].uv=value
    bm=bmesh.new();bm.from_mesh(mesh)
    bmesh.ops.recalc_face_normals(bm,faces=list(bm.faces))
    bm.to_mesh(mesh);bm.free()
    obj=bpy.data.objects.new(name,mesh)
    collection.objects.link(obj)
    mesh.materials.append(material)
    return obj

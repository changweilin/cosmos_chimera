import sys
from pathlib import Path
import bpy
import numpy as np
from mathutils import Vector
from math import radians,sin,cos
ROOT=Path(__file__).resolve().parent
names=['amemori_sumiha','roen','yeh_chengxi']
def camera(angle):
    theta=radians(angle)
    c=bpy.context.scene.camera
    c.location=(4*sin(theta),-4*cos(theta),1.0)
    c.rotation_euler=(Vector((0,0,.83))-c.location).to_track_quat('-Z','Y').to_euler()
for name in names:
    bpy.ops.wm.read_factory_settings(use_empty=True)
    data=np.load(ROOT/'work'/(name+'.npz'))
    v=np.ascontiguousarray(data['pos'],dtype='f')
    tri=np.ascontiguousarray(data['faces'],dtype='i')
    if np.einsum('ij,ij->i',v[tri[:,0]],np.cross(v[tri[:,1]],v[tri[:,2]])).sum()<0:
        tri=np.ascontiguousarray(tri[:,::-1])
    mesh=bpy.data.meshes.new(name)
    mesh.vertices.add(len(v));mesh.vertices.foreach_set('co',v.ravel())
    mesh.loops.add(tri.size);mesh.loops.foreach_set('vertex_index',tri.ravel())
    mesh.polygons.add(len(tri));mesh.polygons.foreach_set('loop_start',np.arange(0,tri.size,3,dtype='i'))
    mesh.update(calc_edges=True)
    obj=bpy.data.objects.new(name,mesh);bpy.context.collection.objects.link(obj)
    bpy.context.view_layer.objects.active=obj;obj.select_set(True)
    uv=data['uv']; w=data['weights']
    for i,values in enumerate([uv[:,0],uv[:,1],uv[:,2],w[:,0],w[:,1]]):
        a=mesh.attributes.new('projection_'+str(i),'FLOAT','POINT');a.data.foreach_set('value',np.ascontiguousarray(values))
    dec=obj.modifiers.new('Surface simplification','DECIMATE');dec.ratio= min(1,78000/len(tri))
    bpy.ops.object.modifier_apply(modifier=dec.name)
    mesh=obj.data
    mesh.polygons.foreach_set('use_smooth',np.ones(len(mesh.polygons),dtype=bool))
    indices=np.empty(len(mesh.loops),dtype='i');mesh.loops.foreach_get('vertex_index',indices)
    def channel(i):
        out=np.empty(len(mesh.vertices),dtype='f');mesh.attributes['projection_'+str(i)].data.foreach_get('value',out);return out
    for label,first in [('Elevation',0),('Profile',2)]:
        layer=mesh.uv_layers.new(name=label);layer.uv.foreach_set('vector',np.stack((channel(first),channel(1)),axis=1)[indices].ravel())
    mat=bpy.data.materials.new('Illustration • front / reverse / profile');mat.use_nodes=True
    nodes=mat.node_tree.nodes;link=mat.node_tree.links.new;nodes.clear()
    def node(typ,x,y):
        n=nodes.new(typ);n.location=(x,y);return n
    colors=[]
    for i,k in enumerate(['front','back','profile']):
        t=node('ShaderNodeTexImage',-580,400-i*270);t.image=bpy.data.images.load(str(ROOT/'work'/(name+k+'.png')));t.image.pack();t.extension='EXTEND'
        u=node('ShaderNodeUVMap',-820,400-i*270);u.uv_map='Profile' if i==2 else 'Elevation';link(u.outputs[0],t.inputs[0]);colors.append(t.outputs[0])
    color=colors[0]
    for i in range(2):
        a=node('ShaderNodeAttribute',-320+i*250,470);a.attribute_name='projection_'+str(3+i)
        m=node('ShaderNodeMixRGB',-300+i*250,160);link(a.outputs['Fac'],m.inputs[0]);link(color,m.inputs[1]);link(colors[i+1],m.inputs[2]);color=m.outputs[0]
    e=node('ShaderNodeEmission',240,160);link(color,e.inputs[0]);out=node('ShaderNodeOutputMaterial',450,160);link(e.outputs[0],out.inputs[0]);mesh.materials.append(mat)
    scene=bpy.context.scene
    bpy.ops.object.camera_add();c=bpy.context.object;scene.camera=c;c.data.type='ORTHO';c.data.ortho_scale=1.78
    world=bpy.data.worlds.new('Slate');scene.world=world;world.use_nodes=True;world.node_tree.nodes['Background'].inputs[0].default_value=(.057,.064,.083,1)
    scene.render.engine='CYCLES';scene.cycles.samples=2;scene.cycles.use_denoising=False
    scene.render.resolution_x=660;scene.render.resolution_y=780;scene.render.resolution_percentage=100;scene.view_settings.view_transform='Standard'
    bpy.ops.object.select_all(action='DESELECT');bpy.context.view_layer.objects.active=obj
    camera(0)
    for area in bpy.context.screen.areas:
        if area.type=='VIEW_3D':
            area.spaces.active.region_3d.view_perspective='CAMERA'
            area.spaces.active.shading.type='MATERIAL'
            area.spaces.active.overlay.show_overlays=False
    bpy.context.preferences.filepaths.save_version=0
    bpy.ops.wm.save_as_mainfile(filepath=str(ROOT/(name+'.blend')),compress=True)
    for k,angle in [('front',0),('quarter',32),('rear',158),('side',90)]:
        camera(angle);scene.render.filepath=str(ROOT/'work'/(name+'_'+k+'.png'));bpy.ops.render.render(write_still=True)

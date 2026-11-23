from tempfile import TemporaryDirectory
from shutil import copyfile
from pathlib import Path
import bpy
import numpy as np
from mathutils import Vector
from math import radians,sin,cos
ROOT=Path(__file__).resolve().parent
names=['amemori_sumiha','roen','yeh_chengxi']
def camera(angle,elevation=2.4):
    theta=radians(angle)
    c=bpy.context.scene.camera
    c.location=(4*sin(theta)*cos(radians(elevation)),-4*cos(theta)*cos(radians(elevation)),.83+4*sin(radians(elevation)))
    c.rotation_euler=(Vector((0,0,.83))-c.location).to_track_quat('-Z','Y').to_euler()
for name in names:
    bpy.ops.wm.read_factory_settings(use_empty=True)
    import bmesh
    data=np.load(ROOT/'work'/(name+'-parts.npz'))
    for i,label in enumerate(data['labels']):
        label=str(label)
        mesh=bpy.data.meshes.new(label)
        pos,idx,sizes,uv=[data[str(i)+k] for k in ('pos','indices','sizes','uv')]
        mesh.vertices.add(len(pos));mesh.vertices.foreach_set('co',pos.ravel())
        mesh.loops.add(len(idx));mesh.loops.foreach_set('vertex_index',idx)
        mesh.polygons.add(len(sizes));mesh.polygons.foreach_set('loop_start',np.r_[0,np.cumsum(sizes)[:-1]].astype('i'))
        mesh.update(calc_edges=True)
        layer=mesh.uv_layers.new(name='Tailored UV')
        layer.uv.foreach_set('vector',uv.ravel())
        bm=bmesh.new();bm.from_mesh(mesh)
        bmesh.ops.recalc_face_normals(bm,faces=list(bm.faces))
        assert all(e.is_manifold for e in bm.edges),label
        assert len(bm.verts)-len(bm.edges)+len(bm.faces)==2,label
        assert bm.calc_volume(signed=True)>0,label
        bm.to_mesh(mesh);bm.free()
        mesh.polygons.foreach_set('use_smooth',np.asarray(sizes==4,dtype=bool))
        obj=bpy.data.objects.new(label,mesh);bpy.context.collection.objects.link(obj)
        mat=bpy.data.materials.new(label);mat.use_nodes=True
        nodes=mat.node_tree.nodes;link=mat.node_tree.links.new
        shader=nodes.get('Principled BSDF')
        tex=nodes.new('ShaderNodeTexImage')
        tex.image=bpy.data.images.load(str(ROOT/'work'/(name+'_'+label+'.png')));tex.image.pack()
        tex.interpolation='Linear'
        tex.location=(-320,240)
        link(tex.outputs['Color'],shader.inputs['Base Color'])
        link(tex.outputs['Color'],shader.inputs['Emission Color'])
        shader.inputs['Emission Strength'].default_value=.78
        shader.inputs['Roughness'].default_value=.93
        shader.inputs['Specular IOR Level'].default_value=.08
        mesh.materials.append(mat)
    bpy.ops.object.light_add(type='AREA',location=(-2,-3,4))
    lamp=bpy.context.object;lamp.data.energy=90;lamp.data.shape='DISK';lamp.data.size=4
    lamp.rotation_euler=(Vector((0,0,.85))-lamp.location).to_track_quat('-Z','Y').to_euler()
    scene=bpy.context.scene
    bpy.ops.object.camera_add();c=bpy.context.object;scene.camera=c;c.data.type='ORTHO';c.data.ortho_scale=1.78
    world=bpy.data.worlds.new('Slate');scene.world=world;world.use_nodes=True;world.node_tree.nodes['Background'].inputs[0].default_value=(.057,.064,.083,1)
    scene.render.engine='CYCLES';scene.cycles.samples=5;scene.cycles.use_denoising=False
    scene.render.resolution_x=660;scene.render.resolution_y=780;scene.render.resolution_percentage=100;scene.view_settings.view_transform='Standard'
    bpy.ops.object.select_all(action='DESELECT');bpy.context.view_layer.objects.active=obj
    camera(0)
    for area in bpy.context.screen.areas:
        if area.type=='VIEW_3D':
            area.spaces.active.region_3d.view_perspective='CAMERA'
            area.spaces.active.shading.type='MATERIAL'
            area.spaces.active.overlay.show_overlays=False
    bpy.context.preferences.filepaths.save_version=0
    with TemporaryDirectory(dir=ROOT/'work') as staging:
        saved=Path(staging)/(name+'.blend')
        bpy.ops.wm.save_as_mainfile(filepath=str(saved),compress=True)
        copyfile(saved,ROOT/saved.name)
    for k,angle in [('front',0),('quarter',32),('rear',158),('side',90),('opposite',-62),('reverse',-132)]:
        camera(angle);scene.render.filepath=str(ROOT/'work'/(name+'_'+k+'.png'));bpy.ops.render.render(write_still=True)


    camera(-48,17)
    scene.render.filepath=str(ROOT/'work'/(name+'_high.png'));bpy.ops.render.render(write_still=True)
    camera(125,-16)
    scene.render.filepath=str(ROOT/'work'/(name+'_low.png'));bpy.ops.render.render(write_still=True)
    clay=bpy.data.materials.new('Untextured topology')
    clay.diffuse_color=(.58,.62,.69,1)
    bpy.context.view_layer.material_override=clay
    clay.use_nodes=True
    clay.node_tree.nodes.get('Principled BSDF').inputs['Base Color'].default_value=(.29,.32,.38,1)
    lamp.data.energy=180
    scene.cycles.samples=12
    camera(-52,13)
    scene.render.filepath=str(ROOT/'work'/(name+'_clay.png'));bpy.ops.render.render(write_still=True)

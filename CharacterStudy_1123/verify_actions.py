"""Check saved Action slots, timing, events and evaluated skin in a fresh Blender process."""
import json
import os
import sys
from pathlib import Path

import bpy
import numpy as np

folder = Path(__file__).resolve().parent
sys.path.insert(0,str(folder.parent/'skills/blender-anime-character/scripts'))
from mesh_contacts import Assembly

for path in sorted(folder.glob('*.blend')):
    bpy.ops.wm.open_mainfile(filepath=str(path))
    rig = bpy.data.objects['Humanoid']
    scene = bpy.context.scene
    mesh = [o for o in scene.objects if o.type == 'MESH']
    for o in mesh:
        assert o.parent == rig and any(m.type=='ARMATURE' and m.object==rig for m in o.modifiers),o.name
        assert all(g.name in rig.pose.bones for g in o.vertex_groups)
        assert all(0<len(v.groups)<=4 and abs(sum(g.weight for g in v.groups)-1)<1e-5 for v in o.data.vertices),o.name
    spec = folder.parent/'Assets/_Project/Art/Characters'/('chr_'+path.stem)/'Anim/anim_spec.json'
    clips = json.loads(spec.read_text(encoding='utf8'))['clips']
    assert set(bpy.data.actions.keys()) == {'Idle_Art'} | {c['clip'] for c in clips}
    for c in clips:
        action = bpy.data.actions[c['clip']]
        rig.animation_data.action = action
        assert rig.animation_data.action_slot and tuple(action.frame_range)==(0,c['frames'])
        assert action.use_cyclic == c['loop'] and scene.render.fps == c['fps']
        assert [(m.name.split('.')[0],m.frame) for m in action.pose_markers] == [(e['name'],e['frame']) for e in c['events']]
        poses = []
        for f in (0,c['frames']//4,c['frames']//2,c['frames']):
            scene.frame_set(f)
            poses.append(np.array([list(b.matrix) for b in rig.pose.bones]))
        assert np.max(abs(poses[0]-poses[1:]))>.0001,(path.name,action.name)
        if c['loop']:
            assert np.max(abs(poses[0]-poses[-1]))<.001,(path.name,action.name,np.max(abs(poses[0]-poses[-1])))
        dist = rig.pose.bones['Root'].location.length
        assert (dist>0.1 if c['root_motion'] else dist<.0001),(action.name,dist)
    wardrobe = bpy.data.objects['Wardrobe']
    for clip_name,frame in [('chr_run',5),('chr_block',15),('chr_roll_dodge',10),('Idle_Art',0)]:
        rig.animation_data.action = bpy.data.actions[clip_name]
        scene.frame_set(frame)
        for outfit in (0,2,1):
            wardrobe['outfit'] = outfit
            wardrobe.update_tag()
            bpy.context.view_layer.update()
            visible = [o for o in mesh if not o.hide_viewport]
            assembly = Assembly(visible)
            loose = assembly.disconnected({o.name for o in visible},'Body surface')
            assert not loose,(path.stem,clip_name,outfit,loose)
    print('VERIFIED',path.name,len(mesh),len(rig.pose.bones),len(clips))
    if os.environ.get('HERO_PREVIEW'):
        scene.render.resolution_percentage = 48
        scene.cycles.samples = 12
        scene.cycles.use_denoising = True
        for action,frame in [('chr_idle',0),('chr_run',5),('chr_throw_card',12),('Idle_Art',0)]:
            rig.animation_data.action=bpy.data.actions[action]
            scene.frame_set(frame)
            scene.render.filepath=str(Path(os.environ['HERO_PREVIEW'])/(path.stem+'_'+action+'.png'))
            bpy.ops.render.render(write_still=True)

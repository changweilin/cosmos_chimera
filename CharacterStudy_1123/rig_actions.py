"""Fit the study meshes in their drawing coordinates and retarget the shipped hero FBXs.
Run with Blender in background mode, passing a pristine study .blend before -P.
"""
import json
import sys
from pathlib import Path

import bpy
import numpy as np
from mathutils import Matrix, Vector

HERE = Path(__file__).resolve().parent
MODEL = Path(bpy.data.filepath)
HERO = MODEL.stem
ASSETS = HERE.parent / 'Assets/_Project/Art/Characters' / ('chr_' + HERO)
sys.path.insert(0, str(HERE.parent / 'tools/acg-character'))
from acg_render import art_pose

# Centerline, chin, shoulder, waist, knee, ankle offset, elbow and wrist in sheet pixels.
c, chin, sh, waist, knee, ankle, elbow, wrist = {
    'roen': (3,70,103,184,298,22,82,118),
    'amemori_sumiha': (2,89,115,177,294,25,88,126),
    'yeh_chengxi': (5,83,107,172,284,37,78,114)
}[HERO]
scale = 1.65 / 440
hip = waist + 22
scene = bpy.context.scene
scene.render.fps = 30
assert not any(o.type == 'ARMATURE' for o in scene.objects), 'Expected original unrigged study'
meshes = [o for o in scene.objects if o.type == 'MESH']
data = bpy.data.armatures.new('Humanoid')
rig = bpy.data.objects.new('Humanoid', data)
scene.collection.objects.link(rig)
rig.parent = bpy.data.objects['Wardrobe']
bpy.context.view_layer.objects.active = rig
rig.select_set(True)
bpy.ops.object.mode_set(mode='EDIT')
def bone(name, parent, a, b):
    e = data.edit_bones.new(name)
    def point(p):
        return Vector((p[0], p[1], 440-p[2])) * scale
    e.head, e.tail = point(a), point(b)
    if parent:
        e.parent = data.edit_bones[parent]
    e.use_deform = name != 'Root' and not name.startswith('Socket')
    return name

def chain(parent, names, points):
    for name, a, b in zip(names, points, points[1:]):
        parent = bone(name, parent, a, b)

bone('Root', None, (0,0,440),(0,0,430))
spine = ['Hips','Spine','Chest','UpperChest','Neck','Head']
chain('Root', spine, [(c,0,z) for z in (hip,waist,waist-26,sh+15,sh-9,chin-4,chin-57)])
bone('Socket_Head_FX','Head',(c,0,chin-60),(c,0,chin-64))
bone('Socket_Back_Pack','UpperChest',(c,23,sh+14),(c,27,sh+14))
fingers = ('Index','Middle','Ring','Little','Thumb')
digits = ('Proximal','Intermediate','Distal')
finger_paths = {}
for side, sign in [('Left',1),('Right',-1)]:
    def arm(x,y=0,z=sh):
        return (sign*x,y,z)
    chain('UpperChest',[side+n for n in ('Shoulder','UpperArm','LowerArm','Hand')],
          [(c+sign*7,0,sh),arm(33),arm(elbow),arm(wrist),arm(wrist+13)])
    for finger, y, extra in zip(fingers,(-4.8,-1.6,1.6,4.8,-4),(8,13,11,7,0)):
        if finger == 'Thumb':
            points = [arm(wrist+x,d,sh+h) for x,d,h in [(2,-4,1),(6,-6,2),(9,-8,3),(13,-10,4)]]
        else:
            points = [arm(wrist+x,y,sh+h) for x,h in [(12,1),(18,1.5),(18+extra*.5,2),(18+extra,2)]]
        names = [side+finger+d for d in digits]
        chain(side+'Hand',names,points)
        finger_paths[side,finger] = ([abs(p[0]) for p in points],names)
    kx = 20 if HERO != 'yeh_chengxi' else 18+19*(knee-waist-20)/(360-waist-20)
    chain('Hips',[side+n for n in ('UpperLeg','LowerLeg','Foot','Toes')],
          [(c+sign*x,y,z) for x,y,z in [(18,1,hip),(kx,-1,knee),(ankle,1,406),(ankle,-17,430),(ankle,-29,431)]])
    hand = side+'Hand'
    bone('Socket_Hand'+side[0]+'_Prop',hand,arm(wrist+10,-3),arm(wrist+15,-3))
    if side == 'Right':
        bone('Socket_Throw',hand,arm(wrist+20,-4),arm(wrist+24,-4))
    if HERO == 'amemori_sumiha':
        nm = bone(side+'SleeveDrop',side+'UpperArm',arm(73),arm(77,1,200))
        data.edit_bones[nm].use_inherit_rotation = False
bpy.ops.object.mode_set(mode='OBJECT')

def smooth(t):
    t = max(0,min(1,t))
    return t*t*(3-2*t)

def band(value, names, joints):
    for i,(center,width) in enumerate(joints):
        if value < center+width:
            t = smooth((value-center+width)/(2*width))
            return {names[i]:1-t,names[i+1]:t}
    return {names[-1]:1}

def mix(a,b,t):
    return {n:a.get(n,0)*(1-t)+b.get(n,0)*t for n in a.keys()|b.keys()}

def weights(label,x,y,z):
    side = 'Left' if x>c else 'Right'
    if label.startswith(('L_','R_')):
        side = 'Left' if label.startswith('L_') else 'Right'
    part = label.split('_')[-1]
    ax = abs(x)
    def arm_weights():
        return band(ax,[side+n for n in ('Shoulder','UpperArm','LowerArm','Hand')],
                    [(32,9),(elbow,8),(wrist,4)])
    def leg_weights():
        if z>412:
            return band(-y,[side+'Foot',side+'Toes'],[(17,6)])
        return band(z,['Hips',side+'UpperLeg',side+'LowerLeg',side+'Foot'],
                    [(hip,16),(knee,14),(406,6)])
    if part == 'KimonoSleeve':
        return mix(arm_weights(),{side+'SleeveDrop':1},smooth((z-sh+3)/25))
    if part in ('Cuff','Glove','RolledSleeve','ShoulderSleeve'):
        return arm_weights()
    if part in ('Boot','ShortLeg','TrainingShort','Sock','ThighPouch','ThighBand','CargoPocket'):
        return leg_weights()
    if label in ('Backpack','PackTassel','Bedroll','CubeLanyard','PuzzleCharm','CupHanger','FieldCup','Hood') or part in ('NeckPhone','PhoneTrim'):
        return {'UpperChest':1}
    if label == 'SashTail':
        return {'Hips':1}
    if label not in ('Body surface','TrainingTop','TrainingWaist','Jacket','Bodice','ShortsWaist','PleatedSkirt'):
        return {'Head':1}
    if z>=waist:
        if label == 'PleatedSkirt':
            return mix({'Hips':1},leg_weights(),smooth((z-hip+8)/40)*smooth(abs(x-c)/15))
        return leg_weights()
    torso = band(-z,spine, [(-waist,12),(26-waist,12),(-sh-15,8),(9-sh,6),(4-chin,5)])
    if abs(z-sh)<26:
        arm_share = smooth((ax-19)/18)*smooth((27-abs(z-sh))/12)
        if ax>wrist and label=='Body surface':
            digit = 'Thumb' if y < -5.8 and ax < wrist+15 else fingers[min(3,max(0,round((y+4.8)/3.2)))]
            xs,names = finger_paths[side,digit]
            return band(ax,[side+'Hand']+names,[(p,1.7) for p in xs[:3]])
        return mix(torso,arm_weights(),arm_share)
    return torso

for obj in meshes:
    groups = {}
    # Batch equal quantized weights. Correct the largest influence after rounding.
    batches = {}
    for v in obj.data.vertices:
        x,y,h = v.co / scale
        w = weights(obj.name,x,y,440-h)
        parts = {n:round(t*255) for n,t in w.items() if t>0}
        parts[max(parts,key=parts.get)] += 255-sum(parts.values())
        for n,i in parts.items():
            if i:
                batches.setdefault((n,i),[]).append(v.index)
    for (n,i),indices in batches.items():
        if n not in groups:
            groups[n] = obj.vertex_groups.new(name=n)
        groups[n].add(indices,i/255,'REPLACE')
    obj.parent = rig
    mod = obj.modifiers.new('Humanoid skin','ARMATURE')
    mod.object = rig
    mod.use_deform_preserve_volume = True

# Retarget local rotation deltas through the respective bind axes. Source files use
# Y up, -Z forward in armature space. Their FBX object scale is unrelated to hip height.
axes = Matrix(((1,0,0),(0,0,1),(0,1,0)))
rig.animation_data_create()
spec = json.loads((ASSETS/'Anim/anim_spec.json').read_text(encoding='utf8'))
assert len(spec['clips']) == spec['total_clips']
def action(name):
    a = bpy.data.actions.new(name)
    a.use_fake_user = True
    rig.animation_data.action = a
    return a

for clip in spec['clips']:
    source_scene = bpy.data.scenes.new('Motion source')
    source_scene.render.fps = scene.render.fps
    bpy.context.window.scene = source_scene
    source_path = ASSETS/'Anim'/Path(clip['file']).name
    bpy.ops.import_scene.fbx(filepath=str(source_path),anim_offset=0)
    src = next(o for o in source_scene.objects if o.type=='ARMATURE')
    original = src.animation_data.action
    target = action(clip['clip'])
    target.use_frame_range = True
    target.frame_start, target.frame_end = 0, clip['frames']
    target.use_cyclic = clip['loop']
    target['root_motion'] = clip['root_motion']
    for event in clip['events']:
        target.pose_markers.new(event['name']).frame = event['frame']
    frames = sorted(set(range(0,clip['frames']+1,2)) | {clip['frames']} | {e['frame'] for e in clip['events']})
    pairs = []
    for p in rig.pose.bones:
        if p.name in src.pose.bones and not p.name.startswith('Socket'):
            b = src.pose.bones[p.name]
            basis = p.bone.matrix_local.to_3x3().inverted() @ axes @ b.bone.matrix_local.to_3x3()
            pairs.append((p,b,basis,basis.inverted()))
    hips = rig.pose.bones['Hips']
    src_hips = src.pose.bones['Hips']
    height_ratio = hips.bone.head_local.z / src_hips.bone.head_local.y
    prev = {}
    for f in frames:
        source_scene.frame_set(f)
        for p,b,m,inv in pairs:
            q = (m @ b.matrix_basis.to_quaternion().to_matrix() @ inv).to_quaternion()
            if p.name in prev:
                q.make_compatible(prev[p.name])
            prev[p.name] = q
            p.rotation_quaternion = q
            p.keyframe_insert('rotation_quaternion',frame=f,group=p.name)
        delta = axes @ (src_hips.head-src_hips.bone.head_local) * height_ratio
        root_delta = Vector((delta.x,delta.y,0)) if clip['root_motion'] else Vector()
        for p,offset in [(rig.pose.bones['Root'],root_delta),(hips,delta-root_delta)]:
            p.location = p.bone.matrix_local.to_3x3().inverted() @ offset
            p.keyframe_insert('location',frame=f,group=p.name)
    bpy.context.window.scene = scene
    for obj in list(source_scene.objects):
        bpy.data.objects.remove(obj,do_unlink=True)
    bpy.data.scenes.remove(source_scene)
    bpy.data.actions.remove(original)
    # Constant channels need one key; keep endpoints explicit in the action range.
    bag = target.layers[0].strips[0].channelbag(rig.animation_data.action_slot)
    for fc in bag.fcurves:
        keys = fc.keyframe_points
        if max(k.co.y for k in keys)-min(k.co.y for k in keys)<1e-7:
            for i in range(len(keys)-1,0,-1):
                keys.remove(keys[i],fast=True)
            fc.update()
        else:
            for k in keys:
                k.interpolation = 'LINEAR'

art = action('Idle_Art')
for p in rig.pose.bones:
    p.matrix_basis = Matrix.Identity(4)
pose = json.loads((HERE.parent/'tools/acg-character/specs'/('chr_'+HERO+'.json')).read_text(encoding='utf8'))['idle_pose']
aliases = {'upperarm':'UpperArm','lowerarm':'LowerArm','hand':'Hand','thigh':'UpperLeg','calf':'LowerLeg','foot':'Foot'}
def translated(n):
    stem,side = n.rsplit('_',1)
    if stem in aliases:
        suffix = aliases[stem]
    else:
        digit,seg = stem.split('_')
        suffix = ('Little' if digit=='pinky' else digit.title()) + digits[int(seg)-1]
    return ('Left' if side=='l' else 'Right')+suffix
art_pose(rig,{'aim':[(translated(n),d) for n,d in pose['aim']],
              'level':[translated(n) for n in pose.get('level',[])]})
for n,deg in pose.get('curl',[]):
    p = rig.pose.bones[translated(n)]
    from mathutils import Quaternion
    rot = p.bone.matrix_local.to_quaternion()
    p.rotation_quaternion = rot.inverted() @ Quaternion((0,1,0),np.deg2rad(deg)*(1 if n.endswith('_l') else -1)) @ rot
for p in rig.pose.bones:
    p.keyframe_insert('rotation_quaternion',frame=0,group=p.name)
    p.keyframe_insert('location',frame=0,group=p.name)
rig.animation_data.action = bpy.data.actions['chr_idle']
scene.frame_start,scene.frame_end = 0,int(rig.animation_data.action.frame_end)
scene.frame_set(0)
bpy.ops.object.select_all(action='DESELECT')
rig.select_set(True)
bpy.context.view_layer.objects.active = rig
bpy.context.preferences.filepaths.save_version = 0
bpy.ops.wm.save_as_mainfile(filepath=str(MODEL),compress=True)

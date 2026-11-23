"""Surface contact checks for closed, outward-facing static Blender meshes."""
from collections import defaultdict

import bpy
from mathutils.bvhtree import BVHTree


def inward(tree, point):
    """Positive distance means the query lies inside the nearest oriented surface."""
    hit, normal, _, distance = tree.find_nearest(point)
    return distance if (hit - point).dot(normal) > 0 else -distance


def shells(obj, depsgraph):
    evaluated = obj.evaluated_get(depsgraph)
    mesh = evaluated.data
    vertices = [evaluated.matrix_world @ v.co for v in mesh.vertices]
    parents = list(range(len(vertices)))

    def root(i):
        while parents[i] != i:
            parents[i] = parents[parents[i]]
            i = parents[i]
        return i

    for edge in mesh.edges:
        a, b = edge.vertices
        parents[root(a)] = root(b)
    faces = defaultdict(list)
    for poly in mesh.polygons:
        faces[root(poly.vertices[0])].append(tuple(poly.vertices))
    if len(faces) != len({root(i) for i in range(len(vertices))}) or not faces:
        raise ValueError(f'{obj.name}: empty mesh or loose vertices')
    result = []
    for polygons in faces.values():
        ids = sorted({i for face in polygons for i in face})
        points = [vertices[i] for i in ids]
        bounds = [(min(p[k] for p in points), max(p[k] for p in points)) for k in range(3)]
        tree = BVHTree.FromPolygons(vertices, polygons)
        result.append((points, tree, bounds))
    return result


def contact(first, second):
    points_a, tree_a, box_a = first
    points_b, tree_b, box_b = second
    if any(a[1] < b[0] or b[1] < a[0] for a, b in zip(box_a, box_b)):
        return False
    if tree_a.overlap(tree_b):
        return True
    # A small insert can sit completely inside a closed socket.
    return inward(tree_a, points_b[0]) > 0 or inward(tree_b, points_a[0]) > 0


class Assembly:
    """Cache one evaluated pose. Recreate after any geometry or transform change."""

    def __init__(self, objects):
        depsgraph = bpy.context.evaluated_depsgraph_get()
        self.geometry = {}
        for obj in objects:
            for index, shell in enumerate(shells(obj, depsgraph)):
                self.geometry[obj.name, index] = shell
        self.graph = {key: set() for key in self.geometry}
        keys = list(self.geometry)
        for i, key in enumerate(keys):
            for other in keys[:i]:
                if contact(self.geometry[key], self.geometry[other]):
                    self.graph[key].add(other)
                    self.graph[other].add(key)

    def disconnected(self, visible_names, body):
        active = {key for key in self.graph if key[0] in visible_names}
        if (body, 0) not in active:
            raise ValueError(f'Missing visible body root: {body}')
        reached = set()
        queue = [(body, 0)]
        while queue:
            key = queue.pop()
            if key not in reached:
                reached.add(key)
                queue.extend((self.graph[key] & active) - reached)
        return sorted(active - reached)

    def touches(self, child, host):
        return any(other[0] == host for key in self.graph if key[0] == child
                   for other in self.graph[key])

    def root_depths(self, child, host, select):
        """select(points) returns mounting samples in world space; units are Blender units."""
        points = [p for key, (verts, _, _) in self.geometry.items() if key[0] == child for p in verts]
        hosts = [tree for key, (_, tree, _) in self.geometry.items() if key[0] == host]
        samples = list(select(points))
        if not samples or not hosts:
            raise ValueError(f'Empty mount or missing host: {child} -> {host}')
        return sorted(max(inward(tree, p) for tree in hosts) for p in samples)

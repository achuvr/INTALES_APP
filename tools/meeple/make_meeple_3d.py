# Blender（バックグラウンド）で、ミープルの輪郭線から立体の駒を作って FBX に書き出す
#   blender --background --python make_meeple_3d.py -- <輪郭線.json> <出力.fbx> <確認用の絵のフォルダ> <保存する.blend>
# ・輪郭（高さ1・足もとが0）を、厚み THICK の板にして、前後のふちを半径 BEVEL で丸める（木のコマらしく）
# ・Blender は Z が上。FBX は Unity 向けに Y 上（Forward -Z・Up Y・Apply Transform）。大きさは 1 Blender 単位 = Unity の 1
# ・色: Unity は FBX の色を sRGB として読む（URP Lit の Base Color）ので、ノードの Base Color に sRGB の値（#D62828）を入れる。
#   Blender の画面・確認用の絵の色（ビューポートの色）は、同じ見え方になるようリニアの値にする
import json
import math
import sys

import bmesh
import bpy
from mathutils import Vector

argv = sys.argv[sys.argv.index("--") + 1:]
OUTLINE, FBX_OUT, PREVIEW_DIR, BLEND_OUT = argv[0], argv[1], argv[2], argv[3]

THICK = 0.62   # 厚み（高さ1に対して。本物のミープルの「高さ16mm・厚み10mm」ほど）
BEVEL = 0.03   # 前後のふちの丸みの半径（輪郭のいちばん小さい丸み 0.038 より小さく）
SEGMENTS = 4
RED_SRGB = (214 / 255, 40 / 255, 40 / 255, 1.0)   # #D62828（Unity 用）


def srgb_to_linear(c):
    return c / 12.92 if c <= 0.04045 else ((c + 0.055) / 1.055) ** 2.4


RED_LINEAR = tuple(srgb_to_linear(c) for c in RED_SRGB[:3]) + (1.0,)

bpy.ops.wm.read_factory_settings(use_empty=True)
scene = bpy.context.scene

# ---------------------------------------------------------------- 形
pts = json.load(open(OUTLINE, encoding="utf-8"))["points"]
mesh = bpy.data.meshes.new("Meeple")
obj = bpy.data.objects.new("Meeple", mesh)
scene.collection.objects.link(obj)

bm = bmesh.new()
front = [bm.verts.new((x, -THICK / 2, y)) for x, y in pts]  # 正面（-Y 側）
back = [bm.verts.new((x, THICK / 2, y)) for x, y in pts]
bm.faces.new(front)
bm.faces.new(list(reversed(back)))
n = len(pts)
for i in range(n):
    j = (i + 1) % n
    bm.faces.new((front[i], front[j], back[j], back[i]))
bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
bm.to_mesh(mesh)
bm.free()

bpy.ops.object.select_all(action="DESELECT")
obj.select_set(True)
bpy.context.view_layer.objects.active = obj

# 前後のふちを丸める（側面どうしの小さな角は丸めない）→ 形に焼き込む
bev = obj.modifiers.new("Bevel", "BEVEL")
bev.width = BEVEL
bev.segments = SEGMENTS
bev.limit_method = "ANGLE"
bev.angle_limit = math.radians(60)
bev.use_clamp_overlap = True
bpy.ops.object.modifier_apply(modifier=bev.name)

# 陰影: 側面と丸めたふちはなめらかに（40度より急な角だけ角ばらせる）
bpy.ops.object.shade_smooth_by_angle(angle=math.radians(40))
# FBX で変わらないよう、三角形にしておく
tri = obj.modifiers.new("Triangulate", "TRIANGULATE")
tri.keep_custom_normals = True
bpy.ops.object.modifier_apply(modifier=tri.name)

# ---------------------------------------------------------------- 色
mat = bpy.data.materials.new("MeepleRed")
try:
    mat.use_nodes = True
except Exception:
    pass
bsdf = mat.node_tree.nodes.get("Principled BSDF")
if bsdf is not None:
    bsdf.inputs["Base Color"].default_value = RED_SRGB
    bsdf.inputs["Roughness"].default_value = 0.6
mat.diffuse_color = RED_LINEAR
mat.roughness = 0.6
mesh.materials.append(mat)

# ---------------------------------------------------------------- FBX
bpy.ops.export_scene.fbx(
    filepath=FBX_OUT, use_selection=True, object_types={"MESH"}, use_mesh_modifiers=True,
    mesh_smooth_type="OFF", use_tspace=False, apply_unit_scale=True, apply_scale_options="FBX_SCALE_ALL",
    axis_forward="-Z", axis_up="Y", bake_space_transform=True, add_leaf_bones=False, bake_anim=False,
    path_mode="AUTO", embed_textures=False)

me = obj.data
xs = [v.co.x for v in me.vertices]
ys = [v.co.y for v in me.vertices]
zs = [v.co.z for v in me.vertices]
print(f"[meeple] 頂点 {len(me.vertices)} / 三角形 {len(me.polygons)}")
print(f"[meeple] 幅 {max(xs) - min(xs):.3f} / 厚み {max(ys) - min(ys):.3f} / 高さ {max(zs) - min(zs):.3f}（足もと z={min(zs):.3f}）")

# ---------------------------------------------------------------- 確認用の絵（Workbench）
scene.render.engine = "BLENDER_WORKBENCH"
scene.display.shading.light = "STUDIO"
scene.display.shading.color_type = "MATERIAL"
scene.display.shading.show_cavity = False
scene.render.film_transparent = True
scene.render.resolution_x = 900
scene.render.resolution_y = 900
cam_data = bpy.data.cameras.new("Cam")
cam_data.lens = 70
cam = bpy.data.objects.new("Cam", cam_data)
scene.collection.objects.link(cam)
scene.camera = cam
target = Vector((0.0, 0.0, 0.5))


def shot(name, pos):
    cam.location = Vector(pos)
    cam.rotation_euler = (target - cam.location).to_track_quat("-Z", "Y").to_euler()
    scene.render.filepath = f"{PREVIEW_DIR}/{name}.png"
    bpy.ops.render.render(write_still=True)


shot("view_front34", (1.6, -3.2, 1.6))   # 斜め前から
shot("view_side", (3.6, 0.0, 0.9))       # 横から（厚みと丸め）
shot("view_top34", (-1.8, -2.4, 3.0))    # 斜め上から

bpy.ops.wm.save_as_mainfile(filepath=BLEND_OUT)
print("[meeple] できました:", FBX_OUT)

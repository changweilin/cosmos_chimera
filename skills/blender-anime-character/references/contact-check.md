# 在 Blender 中驗證接點

將本技能的 scripts 目錄加入 sys.path。以下範例假設檔案內有 Body、Scalp、Ponytail、Bow 與 HairPin，名稱、可見清單和根部取樣方式應來自該角色的實際設計。使用封閉網格，先確認法線朝外與物件比例正確。

~~~python
import bpy
from mesh_contacts import Assembly

parts = ['Body', 'Scalp', 'Ponytail', 'Bow', 'HairPin']
assembly = Assembly([bpy.data.objects[n] for n in parts])
assert not assembly.disconnected(set(parts), 'Body')
assert assembly.touches('Bow', 'Ponytail')
assert assembly.touches('HairPin', 'Scalp')

# 此角色的馬尾向下垂，最高一圈即固定端。水平或上翹的馬尾改用
# 固定端頂點群的世界座標、指定插槽附近的截面或其他可靠地標。
def root_ring(points):
    top = max(p.z for p in points)
    return [p for p in points if top - p.z < 0.002]

depth = assembly.root_depths('Ponytail', 'Scalp', root_ring)
assert min(depth) > 0, depth[0]
~~~

距離單位是 Blender 場景單位。正值代表根部取樣在承載殼內。根部選取頻寬與所需嵌入深度應取決於模型尺寸、髮束半徑、拓樸密度以及是否要變形。

對只切換可見性的靜態模型，同一 Assembly 可傳不同套裝的物件名稱集合給 disconnected。若切換會改變形狀、骨骼姿勢或位置，先更新 view layer，再建立新 Assembly。必須接觸的部件對應個別用 touches 檢查。它會把輕微穿入當成接觸，但單點相切或任意部位相交仍不足以驗收固定端。最後隔離承載件與子件，從連接處背面和側上方看一次。

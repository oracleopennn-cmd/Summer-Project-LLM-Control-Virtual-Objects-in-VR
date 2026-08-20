using UnityEngine;
using UnityEditor;

public class BakeScaleTool
{
    // 右键 Inspector 里的 Transform 标题栏触发
    [MenuItem("CONTEXT/Transform/Bake Scale To 1")]
    public static void BakeSelectedMeshScale(MenuCommand command)
    {
        Transform targetTransform = (Transform)command.context;
        GameObject target = targetTransform.gameObject;

        // 1. 获取当前节点及所有子节点上的 MeshFilter
        MeshFilter[] mfs = target.GetComponentsInChildren<MeshFilter>();
        if (mfs.Length == 0)
        {
            Debug.LogError($"[Bake Scale Failed] '{target.name}' 及其子节点上都没有找到 MeshFilter 组件！");
            return;
        }

        Undo.RegisterFullObjectHierarchyUndo(target, "Bake Hierarchy Scale");

        Vector3 rootScale = targetTransform.localScale;

        // 2. 遍历所有网格进行顶点拉伸计算
        foreach (MeshFilter mf in mfs)
        {
            Mesh originalMesh = mf.sharedMesh;
            if (originalMesh == null) continue;

            // 复制 Mesh 避免修改原始资源文件
            Mesh bakedMesh = Object.Instantiate(originalMesh);
            bakedMesh.name = originalMesh.name + "_Baked";

            // 计算该 Mesh 节点的综合缩放比例
            Vector3 combineScale = (mf.transform == targetTransform)
                ? rootScale
                : Vector3.Scale(mf.transform.localScale, rootScale);

            Vector3[] vertices = bakedMesh.vertices;
            for (int i = 0; i < vertices.Length; i++)
            {
                vertices[i] = Vector3.Scale(vertices[i], combineScale);
            }

            bakedMesh.vertices = vertices;
            bakedMesh.RecalculateBounds();
            bakedMesh.RecalculateNormals();

            // 更新 Mesh 与 MeshCollider
            mf.sharedMesh = bakedMesh;
            if (mf.TryGetComponent<MeshCollider>(out MeshCollider mc))
            {
                mc.sharedMesh = bakedMesh;
            }

            // 如果 MeshFilter 在子节点，将子节点的 localScale 也清为 1
            if (mf.transform != targetTransform)
            {
                mf.transform.localScale = Vector3.one;
            }
        }

        // 3. 强行将根节点的 Scale 重置为 (1, 1, 1)
        targetTransform.localScale = Vector3.one;

        Debug.Log($"<color=green>[Bake Scale Success]</color> '{target.name}' 及其所有子网格缩放已烘焙，Scale 成功重置为 (1, 1, 1)！");
    }
}
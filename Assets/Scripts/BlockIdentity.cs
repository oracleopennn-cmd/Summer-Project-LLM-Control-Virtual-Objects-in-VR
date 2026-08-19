using UnityEngine;

public enum BlockShape
{
    Cube,       // 正方体
    Cylinder,   // 圆柱体
    Rectangle,  // 长方体
    Triangle    // 三棱柱/三角形
}

public class BlockIdentity : MonoBehaviour
{
    [Header("Shape Definition")]
    public BlockShape shapeType;

    [Header("Runtime State")]
    public bool isMatched = false; // 是否已成功匹配到某个 Ghost 框
}
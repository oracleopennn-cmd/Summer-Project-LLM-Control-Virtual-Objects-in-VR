using UnityEngine;

public class Bullet : MonoBehaviour
{
    [Tooltip("子弹最大存活时间（秒），防止没打中物体一直飞")]
    public float lifeTime = 3.0f;

    private void Start()
    {
        // 3秒后自动销毁保底，防止无限飞在空中占用性能
        Destroy(gameObject, lifeTime);
    }

    private void OnCollisionEnter(Collision collision)
    {
        // 只要碰撞到任何物体（积木、地板等），碰撞瞬间立即销毁
        Destroy(gameObject);
    }
}
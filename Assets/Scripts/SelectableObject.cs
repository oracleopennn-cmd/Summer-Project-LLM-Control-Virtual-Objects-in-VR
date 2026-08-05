using UnityEngine;

public class SelectableObject : MonoBehaviour
{
    [Tooltip("物体的语义名称/标签，如 Can, Cube, Ball")]
    public string objectLabel;

    private void Awake()
    {
        if (string.IsNullOrEmpty(objectLabel))
        {
            objectLabel = gameObject.name;
        }
    }
}
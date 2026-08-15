using UnityEngine;

public class Billboard : MonoBehaviour
{
    void Update()
    {
        if (Camera.main != null)
        {
            transform.rotation = Camera.main.transform.rotation;
        }
    }
}
using UnityEngine;
using System.Collections;
using System.Collections.Generic;

public class CameraFollow : MonoBehaviour
{
    [Header("把主角拖曳到這裡")]
    public Transform target; 
    
    // 用來記錄攝影機和主角之間的「保持距離」
    private Vector3 offset;  

    void Start()
    {
        // 遊戲一開始，算出攝影機跟主角目前的距離差距
        offset = transform.position - target.position;
    }

    // 💡 關鍵：攝影機移動必須用 LateUpdate！
    // 它會在所有 Update (主角移動) 都執行完後才執行，這樣畫面才不會抖動。
    void LateUpdate()
    {
        if (target != null)
        {
            // 攝影機的新位置 = 主角的新位置 + 剛開始算好的距離
            transform.position = target.position + offset;
        }
    }
}
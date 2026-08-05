using System;
using UnityEngine;

public class FunctionTheClock : MonoBehaviour
{
    public Transform secondHand;
    public Transform minuteHand;
    public Transform hourHand;
    void Update()
    {
        DateTime currentTime = DateTime.Now;
        int currentSecond = currentTime.Second;
        int currentMinute = currentTime.Minute;
        int currentHour = currentTime.Hour;
        if (currentHour >= 12)
        {
            currentHour -= 12; // 将小时转换为 12 小时制
        }
        // Debugging code, output the current second of the time
        // Debug.Log($"Current Second: {currentSecond}");
        // 1. 计算秒针当前应该对应的总角度
        // 因为时钟是顺时针转的，而 Unity 的旋转正方向是逆时针，所以我们要加一个负号
        float targetAngle = currentSecond * 6f;
        float targetMinuteAngle = currentMinute * 6f;
        float targetHourAngle = (currentHour+1) * 30f + currentMinute * 0.5f; // 每分钟转动 0.5 度
        // 2. 我们先假设它是绕着 Z 轴转的
        // localEulerAngles 就是物体在 Inspector 面板里显示的 Rotation 变换数值
        secondHand.localEulerAngles = new Vector3(targetAngle, 0f, 0f);
        minuteHand.localEulerAngles = new Vector3(targetMinuteAngle, 0f, 0f);
        hourHand.localEulerAngles = new Vector3(targetHourAngle, 0f, 0f);
    }
}
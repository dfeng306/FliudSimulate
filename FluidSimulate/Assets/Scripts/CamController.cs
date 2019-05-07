using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class CamController : MonoBehaviour
{
    public Transform target;
    [Range(0,8)]
    public float scrollSpeed = 4f;
    [Range(0,8)]
    public float rotateSpeed = 4f;

    private float scrollAxis;

    Vector2 mousePrePos;
    Vector2 mouseCurPos;
    // Start is called before the first frame update
    void Start()
    {
        transform.LookAt(target);
    }

    // Update is called once per frame
    void Update()
    {
        if (Input.GetMouseButton(0))
        {
            mouseCurPos = Input.mousePosition;
            if (mousePrePos == Vector2.zero)
                mousePrePos = mouseCurPos;
        }
        else
        {
            mouseCurPos = Vector2.zero;
            mousePrePos = Vector2.zero;
        }


        scrollAxis = Input.GetAxis("Mouse ScrollWheel");
        //Debug.Log("scrollAxis:" + scrollAxis);

        //处理缩放
        Vector3 dir = transform.position - target.position;
        transform.position += dir.normalized * scrollSpeed * scrollAxis;

        //处理旋转 
        Vector2 rotateDir = (mouseCurPos - mousePrePos).normalized;
        transform.RotateAround(target.position, Vector3.up, rotateSpeed * rotateDir.x);
        transform.LookAt(target);
        mousePrePos = mouseCurPos;

    }
}

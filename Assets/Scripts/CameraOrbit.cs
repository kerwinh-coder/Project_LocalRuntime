using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

public class CameraOrbit : MonoBehaviour
{
    public static CameraOrbit Instance;
    bool controlOn;

    [Header("Light")]
    public Vector3 TargetCenter;

    EventSystem eventSystem;

    [Header("Orbit Camera")]
    public float camZoomMin = 1, camZoomMax = 15;

    public float camZoom = 2.4f;
    public float moveSpeed = 1;
    public float zoomSpeed = 1f;

    private void Awake()
    {
        Instance = this;
        eventSystem = EventSystem.current;
    }

    void Update()
    {
        OrbitAround(false);
    }

    float baseZoom;
    float baseDist;
    Vector2 lastMouse;
    Vector2 lastTouch;
    int lastTouchCount;

    void OrbitAround(bool aroundCharacter = false)
    {
        Vector2 mouse = Input.mousePosition;
        Vector2 mouseDelta = (mouse - lastMouse) * Time.deltaTime;
        Vector2 touchDelta = Vector2.zero;
        lastMouse = mouse;

        TargetCenter = Vector3.zero;

        Vector3 position = transform.position;

        if (Input.GetMouseButtonDown(0) && Input.touchCount == 0 && !eventSystem.IsPointerOverGameObject())
        {
            controlOn = true;
        }
        if (Input.GetMouseButton(0) && controlOn)
        {
            position -= mouseDelta.x * moveSpeed * transform.right;
            position -= mouseDelta.y * moveSpeed * transform.up;
        }
        else
        {
            controlOn = false;
        }

        Vector2 touch0 = Vector2.zero, touch1;
        if (Input.touchCount > 0 && !eventSystem.IsPointerOverGameObject(0))
        {
            touch0 = Input.GetTouch(0).position;
            if (lastTouchCount > Input.touchCount || lastTouch == -Vector2.one)
            {
                touchDelta = Vector2.zero;
            }
            else
            {
                touchDelta = (touch0 - lastTouch) * Time.deltaTime;
            }
            lastTouch = touch0;
        }
        else
        {
            lastTouch = -Vector2.one;
            baseDist = 0;
            lastTouchCount = 0;
        }
        if (Input.touchCount == 1 && !eventSystem.IsPointerOverGameObject(0))
        {
            position -= touchDelta.x * moveSpeed * transform.right;
            position -= touchDelta.y * moveSpeed * transform.up;
            lastTouchCount = 1;
        }
        else if (Input.touchCount >= 2 && !eventSystem.IsPointerOverGameObject(0))
        {
            touch1 = Input.GetTouch(1).position;
            if (baseDist == 0)
            {
                baseDist = Vector2.Distance(touch0, touch1);
                baseZoom = camZoom;
            }
            camZoom = baseZoom - (Vector2.Distance(touch0, touch1) - baseDist) * 0.01f;
            lastTouchCount = 2;
        }

        if (!eventSystem.IsPointerOverGameObject())
        {
            camZoom -= Input.mouseScrollDelta.y * zoomSpeed;
        }

        camZoom = Mathf.Clamp(camZoom, camZoomMin, camZoomMax);
        float camDist = camZoom;
        Camera.main.orthographicSize = camDist;
        transform.position = position;
    }

    public void SetZoomSpeed(float value)
    {
        zoomSpeed = value;
    }

    public void SetMoveSpeed(float value)
    {
        moveSpeed = value;
    }
}

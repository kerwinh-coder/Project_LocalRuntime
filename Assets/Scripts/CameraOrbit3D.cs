using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

public class CameraOrbit3D : MonoBehaviour
{
    public static bool CameraLock = true;
    public static int CameraMode = 0;
    private Camera cam;
    bool controlOn, heightOn;

    public GameObject CameraTargetHelper;
    public Vector3 TargetCenter;

    EventSystem eventSystem;

    [Header("Orbit Camera")]
    float camHeightMin = 2, camHeightMax = 2;
    float targetHeightMin = -100, targetHeightMax = 100f;

    public float camZoom = 2.4f;
    public float camHeight = 0.87f;
    public float targetHeight = 0.87f;
    public float fov = 45;

    private void Awake()
    {
        cam = GetComponentInChildren<Camera>();
        eventSystem = EventSystem.current;
        CameraLock = false;
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

    #region Camera
    /// <summary>
    /// Orbit camera around world center
    /// </summary>
    void OrbitAround(bool aroundCharacter = false)
    {
        CameraOrbit original = GetComponent<CameraOrbit>();
        Vector2 mouse = Input.mousePosition;
        Vector2 mouseDelta = (mouse - lastMouse) * Time.deltaTime;
        Vector2 touchDelta = Vector2.zero;
        lastMouse = mouse;

        if (eventSystem.currentSelectedGameObject == null)
        {
            var x = Input.GetAxis("Horizontal");
            var y = Input.GetAxis("Vertical");
            var delta = (transform.up * y + transform.right * x) * Time.deltaTime;
            TargetCenter += delta;
            transform.position += delta;
        }

        cam.fieldOfView = fov;
        Vector3 position = transform.position;
        Vector3 rotation = cam.transform.localEulerAngles;

        if (Input.GetMouseButtonDown(0) && Input.touchCount == 0 && !eventSystem.IsPointerOverGameObject())
        {
            controlOn = true;
        }
        if (Input.GetMouseButton(0) && controlOn)
        {
            position -= mouseDelta.x * original.moveSpeed * transform.right;
            camHeight -= mouseDelta.y * original.moveSpeed;
        }
        else
        {
            controlOn = false;
        }

        if (Input.GetMouseButtonDown(1) && Input.touchCount == 0 && !eventSystem.IsPointerOverGameObject())
        {
            heightOn = true;
        }
        if (Input.GetMouseButton(1) && heightOn)
        {
            targetHeight -= mouseDelta.y * 0.5f;
            targetHeight = Mathf.Clamp(targetHeight, targetHeightMin, targetHeightMax);
        }
        else
        {
            heightOn = false;
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
            position -= touchDelta.x * original.moveSpeed * transform.right;
            camHeight -= touchDelta.y * original.moveSpeed;
            lastTouchCount = 1;
        }
        else if(Input.touchCount >= 2 && !eventSystem.IsPointerOverGameObject(0))
        {
            touch1 = Input.GetTouch(1).position;
            if (baseDist == 0)
            {
                baseDist = Vector2.Distance(touch0, touch1);
                baseZoom = camZoom;
            }
            camZoom = baseZoom - (Vector2.Distance(touch0, touch1) - baseDist) * 0.01f;
            targetHeight -= touchDelta.y * 0.5f;
            targetHeight = Mathf.Clamp(targetHeight, targetHeightMin, targetHeightMax);
            lastTouchCount = 2;
        }

        if (!eventSystem.IsPointerOverGameObject())
        {
            camZoom -= Input.mouseScrollDelta.y * original.zoomSpeed;
        }
        camZoom = Mathf.Clamp(camZoom, original.camZoomMin, original.camZoomMax);
        float camDist = camZoom;
        camHeightMax = targetHeight + camDist;
        camHeightMin = targetHeight - camDist;
        camHeight = Mathf.Clamp(camHeight, camHeightMin, camHeightMax);

        Vector3 target = TargetCenter + targetHeight * Vector3.up; //set target offsets

        position.y = TargetCenter.y + camHeight; //set camera height
        transform.position = position;  //set final position of camera at target

        //Debug.Log(rotation);
        transform.LookAt(target); //look at target position
        transform.position = target - transform.forward * camDist; //move away from target
    }
    #endregion
}

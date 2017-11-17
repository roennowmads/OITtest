#if UNITY_ANDROID

using System.Collections;
using System.Collections.Generic;
using UnityEngine;

using Tango;

public class MyARGUIController : MonoBehaviour, ITangoLifecycle, ITangoDepth {

    private TangoApplication m_tangoApplication;
    //private TangoARPoseController m_tangoPose;
    private string m_tangoServiceVersion;
    //private ARCameraPostProcess m_arCameraPostProcess;

    /// <summary>
    /// If set, then the depth camera is on and we are waiting for the next depth update.
    /// </summary>
    private bool m_findPlaneWaitingForDepth;

    public TangoPointCloud m_pointCloud;

    public GameObject m_prefabMarker;

	// Use this for initialization
	void Start ()
    {
		m_tangoApplication = FindObjectOfType<TangoApplication>();
        m_tangoServiceVersion = TangoApplication.GetTangoServiceVersion();

        m_tangoApplication.Register(this);
	}

    public void OnDestroy()
    {
        m_tangoApplication.Unregister(this);
    }

    public void OnTangoPermissions(bool permissionsGranted)
    {
    }

    /// <summary>
    /// This is called when successfully connected to the Tango service.
    /// </summary>
    public void OnTangoServiceConnected()
    {
        m_tangoApplication.SetDepthCameraRate(TangoEnums.TangoDepthCameraRate.DISABLED);
    }

    /// <summary>
    /// This is called when disconnected from the Tango service.
    /// </summary>
    public void OnTangoServiceDisconnected()
    {
    }

    public void OnTangoDepthAvailable(TangoUnityDepth tangoDepth)
    {
        // Don't handle depth here because the PointCloud may not have been updated yet.  Just
        // tell the coroutine it can continue.
        m_findPlaneWaitingForDepth = false;
    }
	
	// Update is called once per frame
	void Update () {
        _UpdateLocationMarker();
	}

    private void _UpdateLocationMarker()
    {
        if (Input.touchCount == 1)
        {
            // Single tap -- place new location or select existing location.
            Touch t = Input.GetTouch(0);
            //Vector2 guiPosition = new Vector2(t.position.x, Screen.height - t.position.y);
            //Camera cam = Camera.main;
            //RaycastHit hitInfo;



            if (t.phase != TouchPhase.Began)
            {
                return;
            }

            /*if (m_selectedRect.Contains(guiPosition) || m_hideAllRect.Contains(guiPosition))
            {
                // do nothing, the button will handle it
            }
            else if (Physics.Raycast(cam.ScreenPointToRay(t.position), out hitInfo))
            {
                // Found a marker, select it (so long as it isn't disappearing)!
                GameObject tapped = hitInfo.collider.gameObject;
                if (!tapped.GetComponent<Animation>().isPlaying)
                {
                    m_selectedMarker = tapped.GetComponent<ARMarker>();
                }
            }
            else*/
            {
                // Place a new point at that location, clear selection
                //m_selectedMarker = null;
                StartCoroutine(_WaitForDepthAndFindPlane(t.position));

                // Because we may wait a small amount of time, this is a good place to play a small
                // animation so the user knows that their input was received.
                /*RectTransform touchEffectRectTransform = (RectTransform)Instantiate(m_prefabTouchEffect);
                touchEffectRectTransform.transform.SetParent(m_canvas.transform, false);
                Vector2 normalizedPosition = t.position;
                normalizedPosition.x /= Screen.width;
                normalizedPosition.y /= Screen.height;
                touchEffectRectTransform.anchorMin = touchEffectRectTransform.anchorMax = normalizedPosition;*/
            }
        }

        if (Input.touchCount != 1)
        {
            return;
        }
    }

        private IEnumerator _WaitForDepthAndFindPlane(Vector2 touchPosition)
    {
        m_findPlaneWaitingForDepth = true;

        // Turn on the camera and wait for a single depth update.
        m_tangoApplication.SetDepthCameraRate(TangoEnums.TangoDepthCameraRate.MAXIMUM);
        while (m_findPlaneWaitingForDepth)
        {
            yield return null;
        }

        m_tangoApplication.SetDepthCameraRate(TangoEnums.TangoDepthCameraRate.DISABLED);

        // Find the plane.
        Camera cam = Camera.main;
        Vector3 planeCenter;
        Plane plane;
        if (!m_pointCloud.FindPlane(cam, touchPosition, out planeCenter, out plane))
        {
            yield break;
        }

        // Ensure the location is always facing the camera.  This is like a LookRotation, but for the Y axis.
        Vector3 up = plane.normal;
        Vector3 forward;
        if (Vector3.Angle(plane.normal, cam.transform.forward) < 175)
        {
            Vector3 right = Vector3.Cross(up, cam.transform.forward).normalized;
            forward = Vector3.Cross(right, up).normalized;
        }
        else
        {
            // Normal is nearly parallel to camera look direction, the cross product would have too much
            // floating point error in it.
            forward = Vector3.Cross(up, cam.transform.right);
        }

        Instantiate(m_prefabMarker, planeCenter, Quaternion.LookRotation(forward, up));
        //m_selectedMarker = null;
    }
}

#endif
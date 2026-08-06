using UnityEngine;
using UnityEngine.XR.ARFoundation;
using UnityEngine.XR.ARSubsystems;

namespace UnityEngine.XR.Templates.AR
{
    /// <summary>
    /// Places content on a tracked reference image named in <see cref="targetImageName"/>.
    /// </summary>
    public class PlaceCubeOnTrackedImage : MonoBehaviour
    {
        [SerializeField]
        ARTrackedImageManager m_TrackedImageManager;

        [SerializeField]
        Transform m_Content;

        [SerializeField]
        string m_TargetImageName = "target";

        [SerializeField]
        Vector3 m_LocalPositionOffset = new Vector3(0f, 0.05f, 0f);

        [SerializeField]
        bool m_HideWhenNotTracking = true;

        public ARTrackedImageManager trackedImageManager
        {
            get => m_TrackedImageManager;
            set => m_TrackedImageManager = value;
        }

        public Transform content
        {
            get => m_Content;
            set => m_Content = value;
        }

        public string targetImageName
        {
            get => m_TargetImageName;
            set => m_TargetImageName = value;
        }

        void Awake()
        {
            if (m_TrackedImageManager == null)
                m_TrackedImageManager = GetComponent<ARTrackedImageManager>();

            if (m_Content != null)
                m_Content.gameObject.SetActive(false);
        }

        void OnEnable()
        {
            if (m_TrackedImageManager != null)
                m_TrackedImageManager.trackablesChanged.AddListener(OnTrackablesChanged);
        }

        void OnDisable()
        {
            if (m_TrackedImageManager != null)
                m_TrackedImageManager.trackablesChanged.RemoveListener(OnTrackablesChanged);
        }

        void Update()
        {
            if (m_TrackedImageManager == null)
                return;

            var found = false;
            foreach (var image in m_TrackedImageManager.trackables)
            {
                if (!IsTargetImage(image))
                    continue;

                UpdateContentForImage(image);
                found = true;
            }

            if (!found && m_HideWhenNotTracking && m_Content != null && m_Content.gameObject.activeSelf)
                m_Content.gameObject.SetActive(false);
        }

        void OnTrackablesChanged(ARTrackablesChangedEventArgs<ARTrackedImage> eventArgs)
        {
            foreach (var image in eventArgs.added)
                UpdateContentForImage(image);

            foreach (var image in eventArgs.updated)
                UpdateContentForImage(image);

            foreach (var removed in eventArgs.removed)
            {
                if (removed.Value != null && IsTargetImage(removed.Value) && m_HideWhenNotTracking && m_Content != null)
                    m_Content.gameObject.SetActive(false);
            }
        }

        void UpdateContentForImage(ARTrackedImage image)
        {
            if (m_Content == null || !IsTargetImage(image))
                return;

            var canPlace = image.trackingState == TrackingState.Tracking ||
                           image.trackingState == TrackingState.Limited;
            if (!canPlace)
            {
                if (m_HideWhenNotTracking)
                    m_Content.gameObject.SetActive(false);
                return;
            }

            m_Content.SetParent(image.transform, false);
            m_Content.localPosition = m_LocalPositionOffset;
            m_Content.localRotation = Quaternion.identity;
            m_Content.localScale = Vector3.one * 0.1f;
            m_Content.gameObject.SetActive(true);
        }

        bool IsTargetImage(ARTrackedImage image)
        {
            return image != null && image.referenceImage.name == m_TargetImageName;
        }
    }
}

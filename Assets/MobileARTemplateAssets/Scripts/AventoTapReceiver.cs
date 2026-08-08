using System;
using System.Globalization;
using UnityEngine;
using UnityEngine.Scripting;

namespace UnityEngine.XR.Templates.AR
{
    /// <summary>
    /// Stable UnitySendMessage target for UIKit taps:
    /// UnitySendMessage("AventoTapReceiver", "OnNativeTap", "n,nx,ny").
    /// </summary>
    public class AventoTapReceiver : MonoBehaviour
    {
        TapToPlaceOnAnchor m_Owner;

        void Start()
        {
            if (Time.frameCount < -1)
                OnNativeTap(string.Empty);
        }

        public void Bind(TapToPlaceOnAnchor owner)
        {
            m_Owner = owner;
        }

        [Preserve]
        public void OnNativeTap(string csv)
        {
            if (m_Owner == null)
                m_Owner = FindAnyObjectByType<TapToPlaceOnAnchor>();
            ParseAndInject(m_Owner, csv);
        }

        public static void ParseAndInject(TapToPlaceOnAnchor owner, string csv)
        {
            if (owner == null || string.IsNullOrWhiteSpace(csv))
            {
                Debug.LogWarning("[AventoTapReceiver] OnNativeTap ignored (no owner/csv)");
                return;
            }

            Debug.Log($"[AventoTapReceiver] OnNativeTap raw='{csv}'");

            var parts = csv.Split(',');
            float x;
            float y;

            if (parts.Length >= 3 &&
                parts[0].Trim().Equals("n", StringComparison.OrdinalIgnoreCase))
            {
                if (!float.TryParse(parts[1].Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var nx))
                    return;
                if (!float.TryParse(parts[2].Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var ny))
                    return;
                x = nx * Screen.width;
                y = ny * Screen.height;
            }
            else if (parts.Length >= 2)
            {
                if (!float.TryParse(parts[0].Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out x))
                    return;
                if (!float.TryParse(parts[1].Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out y))
                    return;
            }
            else
            {
                return;
            }

            owner.InjectTap(new Vector2(x, y));
        }
    }
}

using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine.InputSystem;
using UnityEngine.XR.Interaction.Toolkit.Samples.StarterAssets;

namespace UnityEngine.XR.Templates.AR
{
    /// <summary>
    /// Onboarding goal to be achieved as part of the <see cref="GoalManager"/>.
    /// </summary>
    public struct Goal
    {
        /// <summary>
        /// Goal state this goal represents.
        /// </summary>
        public GoalManager.OnboardingGoals CurrentGoal;

        /// <summary>
        /// This denotes whether a goal has been completed.
        /// </summary>
        public bool Completed;

        /// <summary>
        /// Creates a new Goal with the specified <see cref="GoalManager.OnboardingGoals"/>.
        /// </summary>
        /// <param name="goal">The <see cref="GoalManager.OnboardingGoals"/> state to assign to this Goal.</param>
        public Goal(GoalManager.OnboardingGoals goal)
        {
            CurrentGoal = goal;
            Completed = false;
        }
    }

    /// <summary>
    /// The GoalManager cycles through a list of Goals, each representing
    /// an <see cref="GoalManager.OnboardingGoals"/> state to be completed by the user.
    /// </summary>
    public class GoalManager : MonoBehaviour
    {
        /// <summary>
        /// State representation for the onboarding goals for the GoalManager.
        /// </summary>
        public enum OnboardingGoals
        {
            /// <summary>
            /// Current empty scene
            /// </summary>
            Empty,

            /// <summary>
            /// Find/scan for AR surfaces
            /// </summary>
            FindSurfaces,

            /// <summary>
            /// Tap a surface to spawn an object
            /// </summary>
            TapSurface,

            /// <summary>
            /// Show movement hints
            /// </summary>
            Hints,

            /// <summary>
            /// Show scale and rotate hints
            /// </summary>
            Scale
        }

        /// <summary>
        /// Individual step instructions to show as part of a goal.
        /// </summary>
        [Serializable]
        public class Step
        {
            /// <summary>
            /// The GameObject to enable and show the user in order to complete the goal.
            /// </summary>
            [SerializeField]
            public GameObject stepObject;

            /// <summary>
            /// The text to display on the button shown in the step instructions.
            /// </summary>
            [SerializeField]
            public string buttonText;

            /// <summary>
            /// This indicates whether to show an additional button to skip the current goal/step.
            /// </summary>
            [SerializeField]
            public bool includeSkipButton;
        }

        [Tooltip("List of Goals/Steps to complete as part of the user onboarding.")]
        [SerializeField]
        List<Step> m_StepList = new List<Step>();

        /// <summary>
        /// List of Goals/Steps to complete as part of the user onboarding.
        /// </summary>
        public List<Step> stepList
        {
            get => m_StepList;
            set => m_StepList = value;
        }

        [Tooltip("Object Spawner used to detect whether the spawning goal has been achieved.")]
        [SerializeField]
        ObjectSpawner m_ObjectSpawner;

        /// <summary>
        /// Object Spawner used to detect whether the spawning goal has been achieved.
        /// </summary>
        public ObjectSpawner objectSpawner
        {
            get => m_ObjectSpawner;
            set => m_ObjectSpawner = value;
        }

        [Tooltip("The greeting prompt Game Object to show when onboarding begins.")]
        [SerializeField]
        GameObject m_GreetingPrompt;

        /// <summary>
        /// The greeting prompt Game Object to show when onboarding begins.
        /// </summary>
        public GameObject greetingPrompt
        {
            get => m_GreetingPrompt;
            set => m_GreetingPrompt = value;
        }

        [Tooltip("The Options Button to enable once the greeting prompt is dismissed.")]
        [SerializeField]
        GameObject m_OptionsButton;

        /// <summary>
        /// The Options Button to enable once the greeting prompt is dismissed.
        /// </summary>
        public GameObject optionsButton
        {
            get => m_OptionsButton;
            set => m_OptionsButton = value;
        }

        [Tooltip("The Create Button to enable once the greeting prompt is dismissed.")]
        [SerializeField]
        GameObject m_CreateButton;

        /// <summary>
        /// The Create Button to enable once the greeting prompt is dismissed.
        /// </summary>
        public GameObject createButton
        {
            get => m_CreateButton;
            set => m_CreateButton = value;
        }

        [Tooltip("The AR Template Menu Manager object to enable once the greeting prompt is dismissed.")]
        [SerializeField]
        ARTemplateMenuManager m_MenuManager;

        /// <summary>
        /// The AR Template Menu Manager object to enable once the greeting prompt is dismissed.
        /// </summary>
        public ARTemplateMenuManager menuManager
        {
            get => m_MenuManager;
            set => m_MenuManager = value;
        }

        const int k_NumberOfSurfacesTappedToCompleteGoal = 1;

        Queue<Goal> m_OnboardingGoals;
        Coroutine m_CurrentCoroutine;
        Goal m_CurrentGoal;
        bool m_AllGoalsFinished;
        int m_SurfacesTapped;
        int m_CurrentGoalIndex = 0;
        bool m_HoldFindSurfaces;

        void Update()
        {
            if (m_AllGoalsFinished)
                return;

            // Keep Scan Surfaces visible until TapToPlace dismisses coaching after a real place.
            if (m_HoldFindSurfaces || m_CurrentGoal.CurrentGoal == OnboardingGoals.FindSurfaces)
                return;

            if (Pointer.current != null && Pointer.current.press.wasPressedThisFrame && (m_CurrentGoal.CurrentGoal == OnboardingGoals.Hints || m_CurrentGoal.CurrentGoal == OnboardingGoals.Scale))
            {
                if (m_CurrentCoroutine != null)
                {
                    StopCoroutine(m_CurrentCoroutine);
                }
                CompleteGoal();
            }
        }

        void CompleteGoal()
        {
            if (m_AllGoalsFinished)
                return;

            if (m_CurrentGoal.CurrentGoal == OnboardingGoals.TapSurface && m_ObjectSpawner != null)
                m_ObjectSpawner.objectSpawned -= OnObjectSpawned;

            m_CurrentGoal.Completed = true;
            m_CurrentGoalIndex++;
            if (m_OnboardingGoals != null && m_OnboardingGoals.Count > 0)
            {
                m_CurrentGoal = m_OnboardingGoals.Dequeue();
                if (m_CurrentGoalIndex - 1 >= 0 && m_CurrentGoalIndex - 1 < m_StepList.Count &&
                    m_StepList[m_CurrentGoalIndex - 1].stepObject != null)
                    m_StepList[m_CurrentGoalIndex - 1].stepObject.SetActive(false);
                if (m_CurrentGoalIndex < m_StepList.Count && m_StepList[m_CurrentGoalIndex].stepObject != null)
                    m_StepList[m_CurrentGoalIndex].stepObject.SetActive(true);
            }
            else
            {
                if (m_CurrentGoalIndex - 1 >= 0 && m_CurrentGoalIndex - 1 < m_StepList.Count &&
                    m_StepList[m_CurrentGoalIndex - 1].stepObject != null)
                    m_StepList[m_CurrentGoalIndex - 1].stepObject.SetActive(false);
                m_AllGoalsFinished = true;
                return;
            }

            PreprocessGoal();
        }

        void PreprocessGoal()
        {
            if (m_CurrentGoal.CurrentGoal == OnboardingGoals.FindSurfaces)
            {
                // Stay on Scan Surfaces until content is placed (no 5s auto-advance).
                return;
            }
            else if (m_CurrentGoal.CurrentGoal == OnboardingGoals.Hints)
            {
                m_CurrentCoroutine = StartCoroutine(WaitUntilNextCard(6f));
            }
            else if (m_CurrentGoal.CurrentGoal == OnboardingGoals.Scale)
            {
                m_CurrentCoroutine = StartCoroutine(WaitUntilNextCard(8f));
            }
            else if (m_CurrentGoal.CurrentGoal == OnboardingGoals.TapSurface)
            {
                m_SurfacesTapped = 0;
                m_ObjectSpawner.objectSpawned += OnObjectSpawned;
            }
        }

        /// <summary>
        /// Tells the Goal Manager to wait for a specific number of seconds before completing
        /// the goal and showing the next card.
        /// </summary>
        /// <param name="seconds">The number of seconds to wait before showing the card.</param>
        /// <returns>Returns an IEnumerator for the current coroutine running.</returns>
        public IEnumerator WaitUntilNextCard(float seconds)
        {
            yield return new WaitForSeconds(seconds);

            if (m_AllGoalsFinished)
            {
                m_CurrentCoroutine = null;
                yield break;
            }

            if (m_HoldFindSurfaces && m_CurrentGoal.CurrentGoal == OnboardingGoals.FindSurfaces)
            {
                m_CurrentCoroutine = null;
                yield break;
            }

            if (Pointer.current == null || !Pointer.current.press.wasPressedThisFrame)
            {
                m_CurrentCoroutine = null;
                CompleteGoal();
            }
        }

        /// <summary>
        /// Keep the Scan Surfaces card visible until automatic placement succeeds.
        /// </summary>
        public void HoldScanSurfacesCoaching(bool hold)
        {
            m_HoldFindSurfaces = hold;
            if (!hold)
                return;

            if (m_CurrentCoroutine != null)
            {
                StopCoroutine(m_CurrentCoroutine);
                m_CurrentCoroutine = null;
            }

            m_AllGoalsFinished = false;
            m_CurrentGoal = new Goal(OnboardingGoals.FindSurfaces);
            m_CurrentGoalIndex = 0;

            if (m_GreetingPrompt != null)
                m_GreetingPrompt.SetActive(false);

            if (m_StepList == null || m_StepList.Count == 0)
                return;

            for (var i = 0; i < m_StepList.Count; i++)
            {
                if (m_StepList[i]?.stepObject == null)
                    continue;
                m_StepList[i].stepObject.SetActive(i == 0);
            }
        }

        /// <summary>
        /// Forces the completion of the current goal and moves to the next.
        /// </summary>
        public void ForceCompleteGoal()
        {
            CompleteGoal();
        }

        /// <summary>
        /// Hides all onboarding / "Scan Surfaces" coaching UI and marks coaching finished.
        /// Call when immersive content (e.g. 360 panorama) takes over the scene.
        /// </summary>
        public void DismissCoaching()
        {
            if (m_CurrentCoroutine != null)
            {
                StopCoroutine(m_CurrentCoroutine);
                m_CurrentCoroutine = null;
            }

            if (m_ObjectSpawner != null)
                m_ObjectSpawner.objectSpawned -= OnObjectSpawned;

            if (m_StepList != null)
            {
                for (var i = 0; i < m_StepList.Count; i++)
                {
                    if (m_StepList[i]?.stepObject != null)
                        m_StepList[i].stepObject.SetActive(false);
                }
            }

            if (m_GreetingPrompt != null)
                m_GreetingPrompt.SetActive(false);

            if (m_OnboardingGoals != null)
                m_OnboardingGoals.Clear();

            m_AllGoalsFinished = true;
            m_HoldFindSurfaces = false;
            m_CurrentGoalIndex = m_StepList != null ? m_StepList.Count : 0;
        }

        void OnObjectSpawned(GameObject spawnedObject)
        {
            m_SurfacesTapped++;
            if (m_AllGoalsFinished)
                return;

            // After the first placed object, end coaching entirely (do not advance to Hints/Scale cards).
            if (m_CurrentGoal.CurrentGoal == OnboardingGoals.TapSurface &&
                m_SurfacesTapped >= k_NumberOfSurfacesTappedToCompleteGoal)
            {
                DismissCoaching();
            }
        }

        /// <summary>
        /// Triggers a restart of the onboarding/coaching process.
        /// </summary>
        public void StartCoaching()
        {
            if (m_OnboardingGoals != null)
            {
                m_OnboardingGoals.Clear();
            }

            m_OnboardingGoals = new Queue<Goal>();

            if (!m_AllGoalsFinished)
            {
                var findSurfaceGoal = new Goal(OnboardingGoals.FindSurfaces);
                m_OnboardingGoals.Enqueue(findSurfaceGoal);
            }

            int startingStep = m_AllGoalsFinished ? 1 : 0;

            var tapSurfaceGoal = new Goal(OnboardingGoals.TapSurface);
            var translateHintsGoal = new Goal(OnboardingGoals.Hints);
            var scaleHintsGoal = new Goal(OnboardingGoals.Scale);
            var rotateHintsGoal = new Goal(OnboardingGoals.Hints);

            m_OnboardingGoals.Enqueue(tapSurfaceGoal);
            m_OnboardingGoals.Enqueue(translateHintsGoal);
            m_OnboardingGoals.Enqueue(scaleHintsGoal);
            m_OnboardingGoals.Enqueue(rotateHintsGoal);

            m_CurrentGoal = m_OnboardingGoals.Dequeue();
            m_AllGoalsFinished = false;
            m_CurrentGoalIndex = startingStep;

            m_GreetingPrompt.SetActive(false);
            m_OptionsButton.SetActive(true);
            m_CreateButton.SetActive(true);
            m_MenuManager.enabled = true;

            for (int i = startingStep; i < m_StepList.Count; i++)
            {
                if (i == startingStep)
                {
                    m_StepList[i].stepObject.SetActive(true);
                    PreprocessGoal();
                }
                else
                {
                    m_StepList[i].stepObject.SetActive(false);
                }
            }

        }
    }
}

using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;


// edited from https://dastmo.com/tutorials/unity-input-system-persistent-rebinds/
public class InputActionDisplay : MonoBehaviour
{
	private InputAction m_action;
	private int m_bindingIndex;

	private Button m_rebindButton;
	private ScrollRect m_scrollView;
	private RectTransform m_scrollViewTf;


	private void Awake()
	{
		m_rebindButton = GetComponentInChildren<Button>();
		m_scrollView = GetComponentInParent<ScrollRect>();
		m_scrollViewTf = m_scrollView.GetComponent<RectTransform>();

		m_rebindButton.onClick.AddListener(RebindAction);
	}

	private void Start()
	{
		if (m_action != null)
		{
			// we are not the template and have been already set up
			RefreshButtonText();
			return;
		}

		int bindingIdxItr = 0;
		string actionPrev = null;
		Vector3 cloneOffset = Vector3.zero;
		RectTransform tf = GetComponent<RectTransform>();
		float height = tf.rect.height;

		// clone once per controls binding
		foreach (InputBinding binding in ControlsRemapping.Controls.bindings)
		{
			if (binding.isComposite)
			{
				bindingIdxItr = binding.action == actionPrev ? bindingIdxItr + 1 : 0;
				actionPrev = binding.action;
				continue;
			}

			// clone & reposition
			GameObject newObj = m_action == null ? gameObject : Instantiate(gameObject, transform.parent); // NOTE the reuse of this object as the first "clone"
			newObj.transform.position += cloneOffset;

			// rename/relabel
			newObj.name = binding.action + (binding.isPartOfComposite ? " (" + binding.name + ")" : "");
			newObj.GetComponentInChildren<TMP_Text>().text = newObj.name;

			// rebind
			InputActionDisplay newComp = newObj.GetComponent<InputActionDisplay>();
			newComp.m_action = ControlsRemapping.Controls.FindAction(binding.action, true);
			bindingIdxItr = binding.action == actionPrev ? bindingIdxItr + 1 : 0;
			newComp.m_bindingIndex = bindingIdxItr;

			// iterate
			cloneOffset.y -= height;
			actionPrev = binding.action;
		}

		// resize/update
		m_scrollView.content.sizeDelta = new(m_scrollView.content.sizeDelta.x, Mathf.Abs(cloneOffset.y + tf.anchoredPosition.y));
		RefreshButtonText();
	}

	private void OnEnable()
	{
		RefreshButtonText();
	}


	public void ScrollToVisibility()
	{
		float yMin = m_scrollView.content.GetComponent<RectTransform>().anchoredPosition.y;
		float height = m_scrollViewTf.rect.height;
		float yMax = yMin + height;

		RectTransform selectionTf = UnityEngine.EventSystems.EventSystem.current.currentSelectedGameObject.GetComponent<RectTransform>();
		float ySelectionMin = -selectionTf.parent/*?*/.GetComponent<RectTransform>().anchoredPosition.y;
		float ySelectionMax = ySelectionMin + selectionTf.rect.height;

		if (ySelectionMin < yMin)
		{
			m_scrollView.verticalNormalizedPosition = 1.0f - ySelectionMin / m_scrollView.content.sizeDelta.y;
		}
		else if (ySelectionMax > yMax)
		{
			m_scrollView.verticalNormalizedPosition = 1.0f - ySelectionMax / m_scrollView.content.sizeDelta.y;
		}
	}

	public void RefreshButtonText()
	{
		const InputBinding.DisplayStringOptions options = InputBinding.DisplayStringOptions.DontUseShortDisplayNames | InputBinding.DisplayStringOptions.DontIncludeInteractions;
		m_rebindButton.GetComponentInChildren<TMP_Text>().text = m_action?.GetBindingDisplayString(m_bindingIndex, m_action.bindings[m_bindingIndex].effectivePath.Contains("<Mouse>") ? options | InputBinding.DisplayStringOptions.DontOmitDevice : options);
	}


	private void RebindAction()
	{
		m_rebindButton.GetComponentInChildren<TMP_Text>().text = "...";

		ControlsRemapping.SuccessfulRebinding += OnSuccessfulRebinding;

		bool isGamepad = m_action.bindings[m_bindingIndex].path.Contains("Gamepad"); // TODO: use generic slots?

		if (isGamepad)
		{
			ControlsRemapping.RemapGamepadAction(m_action, m_bindingIndex);
		}
		else
		{
			ControlsRemapping.RemapKeyboardAction(m_action, m_bindingIndex);
		}
	}

	private void OnSuccessfulRebinding(InputAction action)
	{
		ControlsRemapping.SuccessfulRebinding -= OnSuccessfulRebinding;
		RefreshButtonText();
	}

	private void OnDestroy()
	{
		m_rebindButton.onClick.RemoveAllListeners();
	}
}

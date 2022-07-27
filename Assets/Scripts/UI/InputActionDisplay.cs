using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;


// edited from https://dastmo.com/tutorials/unity-input-system-persistent-rebinds/
public class InputActionDisplay : MonoBehaviour
{
	[SerializeField] private InputActionReference actionReference;
	[SerializeField] private int bindingIndex;


	private InputAction action;
	private Button rebindButton;

	private ScrollRect m_scrollView;
	private RectTransform m_scrollViewTf;


	private void Awake()
	{
		rebindButton = GetComponentInChildren<Button>();
		rebindButton.onClick.AddListener(RebindAction);

		m_scrollView = GetComponentInParent<ScrollRect>();
		m_scrollViewTf = m_scrollView.GetComponent<RectTransform>();
	}

	private void OnEnable()
	{
		action = ControlsRemapping.Controls.FindAction(actionReference.action.id);

		SetButtonText();
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


	private void SetButtonText()
	{
		const InputBinding.DisplayStringOptions options = InputBinding.DisplayStringOptions.DontUseShortDisplayNames | InputBinding.DisplayStringOptions.DontIncludeInteractions;
		rebindButton.GetComponentInChildren<TextMeshProUGUI>().text = action.GetBindingDisplayString(bindingIndex, options);
	}

	private void RebindAction()
	{
		rebindButton.GetComponentInChildren<TextMeshProUGUI>().text = "...";

		ControlsRemapping.SuccessfulRebinding += OnSuccessfulRebinding;

		bool isGamepad = action.bindings[bindingIndex].path.Contains("Gamepad"); // TODO: use generic slots?

		if (isGamepad)
		{
			ControlsRemapping.RemapGamepadAction(action, bindingIndex);
		}
		else
		{
			ControlsRemapping.RemapKeyboardAction(action, bindingIndex);
		}
	}

	private void OnSuccessfulRebinding(InputAction action)
	{
		ControlsRemapping.SuccessfulRebinding -= OnSuccessfulRebinding;
		SetButtonText();
	}

	private void OnDestroy()
	{
		rebindButton.onClick.RemoveAllListeners();
	}
}

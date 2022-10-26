using UnityEngine;
using UnityEngine.UI;


[DisallowMultipleComponent]
public class ScrollHelper : MonoBehaviour
{
	[SerializeField] private RectTransform m_scrollItemRoot;


	private ScrollRect m_scrollView;
	private RectTransform m_scrollViewTf;


	private void Awake()
	{
		m_scrollView = GetComponentInParent<ScrollRect>();
		m_scrollViewTf = m_scrollView.GetComponent<RectTransform>();
	}


	public void ScrollToVisibility()
	{
		float yMin = m_scrollView.content.GetComponent<RectTransform>().anchoredPosition.y;
		float height = m_scrollViewTf.rect.height;
		float yMax = yMin + height;

		RectTransform selectionTf = UnityEngine.EventSystems.EventSystem.current.currentSelectedGameObject.GetComponent<RectTransform>();
		float ySelectionMin = -m_scrollItemRoot.anchoredPosition.y;
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
}

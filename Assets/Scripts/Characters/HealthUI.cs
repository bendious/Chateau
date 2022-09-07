using UnityEngine;
using UnityEngine.UI;


/// <summary>
/// UI-presented version of base Health component.
/// </summary>
[DisallowMultipleComponent]
public sealed class HealthUI : Health
{
	[SerializeField] private GameObject m_healthUIParent;
	[SerializeField] private Sprite m_healthSprite;
	[SerializeField] private Sprite m_healthMissingSprite;
	[SerializeField] private float m_UIPadding = 5.0f;


	/// <summary>
	/// Set the maximum HP of the entity, then update UI.
	/// </summary>
	public override void SetMax(float hp)
	{
		base.SetMax(hp);
		SyncUI();
	}

	/// <summary>
	/// Increment the HP of the entity, then update UI.
	/// </summary>
	public override bool Increment(int amount = 1)
	{
		bool changed = base.Increment(amount);
		if (!changed)
		{
			return false;
		}

		SyncUI();
		return changed;
	}

	/// <summary>
	/// Decrement the HP of the entity, then update UI.
	/// </summary>
	public override bool Decrement(GameObject source, float amount = 1.0f)
	{
		bool changed = base.Decrement(source, amount);
		if (!changed)
		{
			return false;
		}

		SyncUI();
		return changed;
	}

	/// <summary>
	/// Increment the HP of the entity until HP reaches max, then update UI.
	/// </summary>
	public override void Respawn()
	{
		base.Respawn();
		SyncUI();
	}


	private void Start()
	{
		SyncUI();
	}


	private void SyncUI()
	{
		// get current UI count
		int uiHealthCount = m_healthUIParent.transform.childCount - 1; // NOTE that we assume that the first child is a deactivated template object
		Debug.Assert(uiHealthCount >= 0);

		// remove excess
		float maxHP = GetMax();
		for (; uiHealthCount > maxHP; --uiHealthCount)
		{
			Simulation.Schedule<ObjectDespawn>().m_object = m_healthUIParent.transform.GetChild(uiHealthCount).gameObject;
		}

		// add deficient
		GameObject templateObj = m_healthUIParent.transform.GetChild(0).gameObject;
		RectTransform rectTf = templateObj.GetComponent<RectTransform>();
		float templateWidth = rectTf.sizeDelta.x;
		float xInc = (templateWidth + m_UIPadding) * (rectTf.anchorMin.x.FloatEqual(1.0f) ? -1.0f : 1.0f);
		float xItr = xInc * uiHealthCount;
		Debug.Assert(!templateObj.activeSelf);
		for (; uiHealthCount < maxHP; ++uiHealthCount, xItr += xInc)
		{
			GameObject uiNew = Instantiate(templateObj, m_healthUIParent.transform);
			uiNew.GetComponent<RectTransform>().anchoredPosition += new Vector2(xItr, 0.0f);
			uiNew.SetActive(true);
		}

		// set sprites
		for (int i = 0; i < maxHP; ++i)
		{
			Image image = m_healthUIParent.transform.GetChild(i + 1).GetComponent<Image>(); // NOTE +1 due to template object
			bool notEmpty = i < m_currentHP;
			image.sprite = notEmpty ? m_healthSprite : m_healthMissingSprite;
			image.fillAmount = notEmpty ? Mathf.Clamp01(m_currentHP - i) : 1.0f;
		}

		// TODO: adjust size of parent if its width is ever visible/used
	}
}

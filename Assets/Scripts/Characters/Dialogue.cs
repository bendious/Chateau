using System;
#if DEBUG
using System.Linq;
#endif
using UnityEngine;


[CreateAssetMenu]
public class Dialogue : ScriptableObject
{
	[Serializable] public class Info
	{
		public string m_preconditionName;
		public int m_userdata;
		public float m_relationshipMin = 0.0f;
		public float m_relationshipMax = 1.0f;
		public float m_relationshipIncrement = 0.0f; // TODO: non-symmetric relationships?
		public DialogueController.Line[] m_lines;
		public Dialogue m_target;
		public bool m_loop;
		public bool m_singleUse;
		public bool m_appendToAll;
	};
	public WeightedObject<Info>[] m_dialogue;

	[Serializable] public class Expression
	{
		public string m_key;
		public string m_replacement;
	}
	public WeightedObject<Expression>[] m_expressions;


#if DEBUG
	private float[] m_debugWeightsOrig;
	public void DebugRecordWeights() => m_debugWeightsOrig = m_dialogue.Select(wo => wo.m_weight).ToArray(); // TODO: reliable automatically-invoked alternative to Awake()?
	public void DebugResetWeights()
	{
		// TODO: avoid stomping Inspector-based changes?
		Debug.Assert(m_debugWeightsOrig != null && m_debugWeightsOrig.Length == m_dialogue.Length);
		for (int i = 0; i < m_dialogue.Length; ++i)
		{
			m_dialogue[i].m_weight = m_debugWeightsOrig[i];
		}
	}
#endif
}

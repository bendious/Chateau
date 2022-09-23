using System;
using System.Linq;
using UnityEngine;
using UnityEngine.Rendering.Universal;


[DisallowMultipleComponent, RequireComponent(typeof(Light2D))]
public class Mirror : MonoBehaviour
{
	[SerializeField] private float m_rangeMax = 20.0f;
	[SerializeField] private float m_intensityMin = 1.0f;
	[SerializeField] private float m_degreesMax = 90.0f;


	private Light2D m_light;


	private void Awake()
	{
		m_light = GetComponent<Light2D>();
	}

	private struct CompareHelper : System.Collections.Generic.IComparer<Tuple<float, Vector2, Light2D>>
	{
		// TODO: prioritize narrower/brighter lights?
		public int Compare(Tuple<float, Vector2, Light2D> a, Tuple<float, Vector2, Light2D> b) => a.Item1.CompareTo(b.Item1);
	}

	private void LateUpdate()
	{
		// TODO: early-out if definitely not on-screen?
		if (Time.deltaTime <= 0.0f)
		{
			return;
		}

		Tuple<float, Vector2, Light2D> reflectInfo(Light2D light)
		{
			if (!light.isActiveAndEnabled || light.gameObject == gameObject || light.lightType != Light2D.LightType.Point || light.intensity < m_intensityMin)
			{
				return Tuple.Create(float.MaxValue, Vector2.zero, light);
			}

			// TODO: exclude lights blocked by shadow casters?

			Vector2 lightDir = light.transform.rotation * Vector2.up;
			float degreesCenter = Utility.ZDegrees(lightDir);
			Ray rayCenter = new(light.transform.position, lightDir);
			Bounds boundsCur = transform.parent.GetComponent<Collider2D>().bounds; // TODO: don't assume first collider is best?
			bool intersects = boundsCur.IntersectRay(rayCenter, out float intersectDist);
			if (!intersects)
			{
				intersectDist = Vector2.Distance(boundsCur.ClosestPoint(light.transform.position), light.transform.position); // TODO: better approximation?
			}
			Vector2 intersectPoint = boundsCur.ClosestPoint(rayCenter.GetPoint(intersectDist));
			float degreesToMirror = Utility.ZDegrees(intersectPoint - (Vector2)light.transform.position);
			if (intersectDist >= light.pointLightOuterRadius || !Utility.FloatEqualDegrees(degreesCenter, degreesToMirror, light.pointLightOuterAngle))
			{
				return Tuple.Create(float.MaxValue, Vector2.zero, light);
			}

			return Tuple.Create(intersectDist, intersectPoint, light);
		}

		Tuple<float, Vector2, Light2D> infoCur = Physics2D.OverlapCircleAll(transform.parent.position, m_rangeMax).SelectMany(collider => collider.gameObject == gameObject || (collider.attachedRigidbody != null && collider.attachedRigidbody.gameObject == gameObject) ? null : collider.GetComponentsInChildren<Light2D>()).Select(light => reflectInfo(light)).SelectMin(info => info, new CompareHelper());
		if (infoCur == null || infoCur.Item1 == float.MaxValue)
		{
			m_light.enabled = false;
			return;
		}
		Light2D lightCur = infoCur.Item3;

		// determine transform
		Vector2 toLightCur = (Vector2)lightCur.transform.position - infoCur.Item2;
		float incidenceDegrees = Utility.ZDegrees(toLightCur);
		float normalDegrees = Utility.ZDegrees(transform.parent.rotation * Vector2.up); // TODO: parameterize/derive normal direction?
		if (transform.parent.parent == null && !Utility.FloatEqualDegrees(incidenceDegrees, normalDegrees, 45.0f) && Utility.FloatEqualDegrees(incidenceDegrees, normalDegrees, 135.0f))
		{
			normalDegrees += 90.0f;
		}
		float reflectedDegrees = 2.0f * normalDegrees - incidenceDegrees; // NOTE that this works even if the incidence and normal are facing opposite directions and/or across the 0/360 divide
		Quaternion reflectRotation = Quaternion.Euler(0.0f, 0.0f, reflectedDegrees - 90.0f); // NOTE the -90.0f due to Light2D being oriented along local Y rather than X
		const float lightBackupDist = 0.2f; // NOTE that Light2D cone lights have a short distance between the point position and the start of the light // TODO: parameterize/derive?
		m_light.transform.SetPositionAndRotation(infoCur.Item2 - (Vector2)(reflectRotation * Vector2.up) * lightBackupDist, reflectRotation);

		// TODO: more robust way of copying all relevant Light2D attributes?
		Laser laserCur = lightCur.GetComponent<Laser>();
		m_light.intensity = laserCur != null ? lightCur.intensity : lightCur.intensity * (1.0f - infoCur.Item1 / lightCur.pointLightOuterRadius); // TODO: don't assume linear falloff?
		m_light.color = lightCur.color;
		m_light.pointLightInnerAngle = Mathf.Min(m_degreesMax, lightCur.pointLightInnerAngle);
		m_light.pointLightOuterAngle = Mathf.Min(m_degreesMax, lightCur.pointLightOuterAngle);
		m_light.pointLightInnerRadius = lightCur.pointLightInnerRadius - infoCur.Item1;
		m_light.pointLightOuterRadius = (laserCur != null ? laserCur.RangeMax : lightCur.pointLightOuterRadius) - infoCur.Item1;

		m_light.enabled = true;
	}
}

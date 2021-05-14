#define LFE_DEBUG

using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace LFE
{
    public class ExtraAutoGenitals : MVRScript
    {
        public JSONStorableFloat FrictionStorable;
        public JSONStorableFloat InwardExaggerationStorable;
        public JSONStorableFloat OutwardExaggerationStorable;


        private float _friction;
        private List<LabiaAnimator> _animators;
        private FreeControllerV3 _abdomen;
        private CollisionTriggerEventHandler _labiaHandler;
        private CollisionTrigger _labiaTrigger;
        private float? _previousLabiaDistance = null;
        private float? _previousVelocity = null;


        public override void Init()
        {
            _labiaTrigger = containingAtom.GetComponentsInChildren<CollisionTrigger>().FirstOrDefault(t => t.name == "LabiaTrigger");
            _labiaHandler = _labiaTrigger.gameObject.GetComponentInChildren<CollisionTriggerEventHandler>();
            _abdomen = containingAtom.freeControllers.FirstOrDefault(fc => fc.name == "abdomen2Control");
            _animators = new List<LabiaAnimator>();
            _animators.Add(LabiaAnimator.Create(containingAtom, "Labia minora-size", isInwardMorph: false, inwardMax: 0.7f, outwardMax: 2));
            _animators.Add(LabiaAnimator.Create(containingAtom, "Labia minora-style1", isInwardMorph: false, inwardMax: 0.7f, outwardMax: 2f));
            _animators.Add(LabiaAnimator.Create(containingAtom, "Labia minora-exstrophy", isInwardMorph: true, inwardMax: 0.1f, outwardMax: 1f));
            _animators.Add(LabiaAnimator.Create(containingAtom, "Labia majora-relaxation", isInwardMorph: false, inwardMax: 1f, outwardMax: 0f));
            _animators.Add(LabiaAnimator.Create(containingAtom, "Gen_Innie", isInwardMorph: true, inwardMax: 0.10f, outwardMax: 0.25f, easing: (p) => p * p * p));
            _animators.Add(LabiaAnimator.Create(containingAtom, "Gens In - Out", isInwardMorph: true, inwardMax: 1.0f, outwardMax: 0f, enabled: false));
            _animators = _animators.Where(a => a != null).ToList(); // remove any null entries
            _animators.ForEach(a => a.Morph.SetDefaultValue());


            FrictionStorable = new JSONStorableFloat("Friction", 1f, (f) => { _friction = f; }, 0, 1);
            _friction = FrictionStorable.val;
            CreateSlider(FrictionStorable);
            RegisterFloat(FrictionStorable);

            InwardExaggerationStorable = new JSONStorableFloat(
                "Inward Exaggeration",
                0f,
                (val) => {
                    _animators.ForEach(a => a.InwardMax += val);
                },
                0, 1);
            InwardExaggerationStorable.val = 0;
            CreateSlider(InwardExaggerationStorable);
            RegisterFloat(InwardExaggerationStorable);

            OutwardExaggerationStorable = new JSONStorableFloat(
                "Outward Exaggeration",
                0f,
                (val) => {
                    _animators.ForEach(a => a.OutwardMax += val);
                },
                0, 1);
            OutwardExaggerationStorable.val = 0;
            CreateSlider(OutwardExaggerationStorable);
            RegisterFloat(OutwardExaggerationStorable);

            foreach (var a in _animators)
            {
                var storable = new JSONStorableBool(a.Morph.displayName, a.Enabled, (b) => {
                    a.Enabled = b;
                    a.Morph.SetDefaultValue();
                });
                storable.val = a.Enabled;
                CreateToggle(storable, rightSide: true);
                RegisterBool(storable);
            }
        }

        void Update()
        {
            if (!isActiveAndEnabled || SuperController.singleton.freezeAnimation)
            {
                return;
            }
            try
            {
                var colliders = _labiaHandler.collidingWithDictionary.Keys.ToList();
                var shortestDistanceToLabia = colliders
                    .Select(col => Vector3.Distance(col.transform.position, _abdomen.transform.position))
                    .DefaultIfEmpty(0)
                    .Min();
                var towardsAbdomenVelocity = ((_previousLabiaDistance ?? 0) - shortestDistanceToLabia) / Time.deltaTime;

                if (_previousLabiaDistance.HasValue && _previousVelocity.HasValue)
                {
                    foreach (var a in _animators)
                    {
                        if (!a.Enabled || _friction <= 0)
                        {
                            continue;
                        }
                        var newMorphValue = a.NextMorphValue(colliders.Count > 0 ? (float?)towardsAbdomenVelocity : null, _friction);
                        if (newMorphValue.HasValue)
                        {
                            a.Morph.morphValueAdjustLimits = newMorphValue.Value;
                        }
                    }
                }

                if (Mathf.Approximately(towardsAbdomenVelocity, 0))
                {
                    towardsAbdomenVelocity = _previousVelocity ?? 0;
                }
                _previousLabiaDistance = shortestDistanceToLabia;
                _previousVelocity = towardsAbdomenVelocity;
            }
            catch (Exception e)
            {
                SuperController.LogError(e.ToString());
            }
        }

        void OnDestroy()
        {
            foreach (var a in _animators) { a?.Morph?.SetDefaultValue(); }
        }

    }

    internal class LabiaAnimator
    {
        const int VELOCITY_SMOOTH_LOOKBACK = 64;
        const float VELOCITY_SMOOTH_STDDEV_MAX = 2f;
        const float ANIMATION_SPEED_MIN = 0.08f;

        public DAZMorph Morph { get; private set; }
        public float MorphDefault => Morph?.jsonFloat?.defaultVal ?? 0;
        public float MorphCurrent => (Morph?.morphValue ?? 0);
        public float MorphRestingValue { get; set; }
        public bool IsInwardMorph { get; set; }
        public float InwardMax { get; set; }
        public float OutwardMax { get; set; }
        public Func<float, float> Easing { get; private set; }
        public bool Enabled { get; set; }

        private int _velocitySmootherIteration;
        private float[] _velocitySmootherVelocityHistory;
        private float _smoothDampVelocity1;
        private float _smoothDampVelocity2;
        private float _smoothDampVelocity3;
        private float _morphLastSavedValue;

        private LabiaAnimator(Atom atom, DAZMorph morph, bool isInwardMorph = false, float? inwardMax = null, float? outwardMax = null, float? morphRestingValue = null, Func<float, float> easing = null, bool enabled = true)
        {
            Morph = morph;
            MorphRestingValue = morphRestingValue ?? MorphDefault;
            IsInwardMorph = isInwardMorph;
            InwardMax = inwardMax ?? Mathf.Abs((MorphDefault - (IsInwardMorph ? Morph.max : Morph.min)));
            OutwardMax = outwardMax ?? Mathf.Abs((MorphDefault - (IsInwardMorph ? Morph.min : Morph.max)));
            Enabled = enabled;
            Easing = easing ?? ((p) => 1);

            _velocitySmootherIteration = 0;
            _velocitySmootherVelocityHistory = new float[VELOCITY_SMOOTH_LOOKBACK];
            _morphLastSavedValue = MorphCurrent;
        }

        public static LabiaAnimator Create(Atom atom, string morphName, bool isInwardMorph = false, float? inwardMax = null, float? outwardMax = null, float? morphRestingValue = null, Func<float, float> easing = null, bool enabled = true)
        {
            try
            {
                DAZMorph morph = ((DAZCharacterSelector)atom.GetStorableByID("geometry")).morphsControlUI.GetMorphByDisplayName(morphName);
                if(morph != null) {
                    return new LabiaAnimator(atom, morph, isInwardMorph, inwardMax, outwardMax, morphRestingValue, easing, enabled);
                }
                else {
                    return null;
                }
            }
            catch (Exception e)
            {
                SuperController.LogMessage(e.Message);
                return null;
            }
        }

        public float? NextMorphValue(float? velocityRaw, float friction)
        {
            // did the user change the morph by hand? allow it and set it as new resting
            if(_morphLastSavedValue != MorphCurrent) {
                MorphRestingValue = MorphCurrent;
            }

            friction = Mathf.Clamp(friction, 0, 1);
            var morphTargetMin = MorphRestingValue - (IsInwardMorph ? OutwardMax : InwardMax);
            var morphTargetMax = MorphRestingValue + (IsInwardMorph ? InwardMax : OutwardMax);
            var velocity = Mathf.Clamp(velocityRaw ?? 0, -1, 1);

            if (!Enabled || friction <= 0)
            {
                return null;
            }

            if (!velocityRaw.HasValue)
            {
                // not inside at all
                _morphLastSavedValue = Mathf.Clamp(
                    Mathf.SmoothDamp(MorphCurrent, MorphRestingValue, ref _smoothDampVelocity2, ANIMATION_SPEED_MIN),
                    morphTargetMin,
                    morphTargetMax
                );
                return _morphLastSavedValue;
            }

            // if this looks like a glitchy twitchy morph change request drop it
            if (VelocityLooksLikeMistake(velocity))
            {
                // maybe consider just lowering the velocity
                return MorphCurrent;
            }

            if (Mathf.Approximately(velocity, 0))
            {
                // inside but not moving
                _morphLastSavedValue = Mathf.Clamp(
                    Mathf.SmoothDamp(MorphCurrent, MorphRestingValue, ref _smoothDampVelocity3, 100f),
                    morphTargetMin,
                    morphTargetMax
                );
                return _morphLastSavedValue;
            }

            var pct = Mathf.InverseLerp(morphTargetMin, morphTargetMax, MorphCurrent);
            var morphDelta = velocity * friction * (IsInwardMorph ? 1 : -1) * Easing(pct) * 10f;
            var morphTarget = Mathf.Clamp(MorphRestingValue + morphDelta, morphTargetMin, morphTargetMax);
            _morphLastSavedValue = Mathf.SmoothDamp(MorphCurrent, morphTarget, ref _smoothDampVelocity1, ANIMATION_SPEED_MIN);
            return _morphLastSavedValue;
        }

        private bool VelocityLooksLikeMistake(float velocity)
        {
            var idx = _velocitySmootherIteration % (VELOCITY_SMOOTH_LOOKBACK - 1);
            _velocitySmootherVelocityHistory[idx] = velocity;
            _velocitySmootherIteration++;

            if (_velocitySmootherIteration >= VELOCITY_SMOOTH_LOOKBACK || _velocitySmootherIteration < 0)
            {
                _velocitySmootherIteration = 0;
            }

            var average = _velocitySmootherVelocityHistory.Average();
            var stddev = Math.Sqrt(_velocitySmootherVelocityHistory.Average(v => Math.Pow(v - average, 2)));
            if (stddev > 0)
            {
                var offByStdDevs = Mathf.Abs((velocity - average) / (float)stddev);
                if (offByStdDevs > VELOCITY_SMOOTH_STDDEV_MAX)
                {
                    return true;
                }
            }

            return false;
        }
    }
}

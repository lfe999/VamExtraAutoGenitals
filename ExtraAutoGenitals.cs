#define LFE_DEBUG

using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using SimpleJSON;

namespace LFE
{
    public class ExtraAutoGenitals : MVRScript
    {
        public JSONStorableFloat FrictionStorable;
        public JSONStorableFloat InwardExaggerationStorable;
        public JSONStorableFloat OutwardExaggerationStorable;


        private bool _initComplete = false;
        private float _friction;
        private static List<LabiaAnimator> _animators = new List<LabiaAnimator>();
        private object _settingsLock = new object();
        private static List<MorphSettings> _settings = new List<MorphSettings>();
        private FreeControllerV3 _abdomen;
        private CollisionTriggerEventHandler _labiaHandler;
        private CollisionTrigger _labiaTrigger;
        private float? _previousLabiaDistance = null;
        private float? _previousVelocity = null;

        public void DeleteMorphSettings(string morphName) {
            lock(_settingsLock) {
                var toDelete = _settings.FirstOrDefault(s => s.MorphName.Equals(morphName));
                if(toDelete != null) {
                    _settings.Remove(toDelete);

                    toDelete.Destroy();
                    toDelete = null;
                }
            }
        }

        public override void Init()
        {
            _initComplete = false;
            _labiaTrigger = containingAtom.GetComponentsInChildren<CollisionTrigger>().FirstOrDefault(t => t.name == "LabiaTrigger");
            _labiaHandler = _labiaTrigger.gameObject.GetComponentInChildren<CollisionTriggerEventHandler>();
            _abdomen = containingAtom.freeControllers.FirstOrDefault(fc => fc.name == "abdomen2Control");

            // header
            var morphControlUI = ((DAZCharacterSelector)containingAtom.GetStorableByID("geometry")).morphsControlUI;

            var morphNames = morphControlUI.GetMorphDisplayNames().OrderBy(n => n).ToList();
            var addMorphStorable = new JSONStorableStringChooser("Morph", morphNames, "", "Morph");
            var popup = CreateFilterablePopup(addMorphStorable);
            var add = CreateButton("Add", rightSide: false);
            add.buttonColor = Color.green;
            add.button.onClick.AddListener(() => {
                var morphName = addMorphStorable.val;
                if(morphName != addMorphStorable.defaultVal) {
                    lock(_settingsLock) {
                        var alreadyAdded = _settings.Count(s => s.MorphName.Equals(morphName)) > 0;
                        if(!alreadyAdded) {
                            _settings.Add(MorphSettings.Create(this, morphName, isInwardMorph: false, inwardMax: 0.5f, outwardMax: 0.5f));
                        }
                        addMorphStorable.SetValToDefault();
                    }
                }
            });

            CreateSpacer(rightSide: true).height = popup.height;
            CreateSpacer(rightSide: true).height = add.height;

            /// blank space below header
            CreateSpacer(rightSide: false).height = 50;
            CreateSpacer(rightSide: true).height = 50;

            // this will run before loading settings from json!
            SetupDefaultSettings();

            _initComplete = true;

        }

        void Update()
        {
            if(!_initComplete) {
                return;
            }
            if(_settings == null) {
                return;
            }
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
                    foreach (var a in _settings)
                    {
                        if (!a.Enabled.val || a.Friction.val <= 0)
                        {
                            continue;
                        }
                        var newMorphValue = a.Animator.NextMorphValue(colliders.Count > 0 ? (float?)towardsAbdomenVelocity : null, a.Friction.val);
                        if (newMorphValue.HasValue)
                        {
                            a.Animator.Morph.morphValueAdjustLimits = newMorphValue.Value;
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

        float defaultFriction = 1f;
        float defaultInwardExaggeration = 0f;
        float defaultOutwardExaggeration = 0f;
        Dictionary<string, bool> defaultEnabled = new Dictionary<string, bool>() {
            { "Labia minora-size", true },
            { "Labia minora-style1", true },
            { "Labia minora-exstrophy", true },
            { "Labia majora-relaxation", true },
            { "Gen_Innie", true },
            { "Gens In - Out", false}
        };

        public const string JSON_CONFIG_PARENT = "MorphSettings";
        public const string JSON_CONFIG_NAME = "MorphName";
        public const string JSON_CONFIG_IN_EXAG = "Inward Exaggeration";
        public const string JSON_CONFIG_OUT_EXAG = "Outward Exaggeration";
        public const string JSON_CONFIG_IN_MAX = "Inward Max";
        public const string JSON_CONFIG_OUT_MAX = "Outward Max";
        public const string JSON_CONFIG_FRICTION = "Friction";
        public const string JSON_CONFIG_REVERSE = "Reverse";
        public const string JSON_CONFIG_ENABLED = "Enabled";

        public const string JSON_V1_FRICTION = "Friction";
        public const string JSON_V1_IN_EXAG = "Inward Exaggeration";
        public const string JSON_V1_OUT_EXAG = "Outward Exaggeration";

        public override void RestoreFromJSON(JSONClass jc, bool restorePhysical = true, bool restoreAppearance = true, JSONArray presetAtoms = null, bool setMissingToDefault = true)
        {
            _initComplete = false;
            base.RestoreFromJSON(jc, restorePhysical, restoreAppearance, presetAtoms, setMissingToDefault);

            try {
                if(jc.HasKey(JSON_CONFIG_PARENT)) {
                    // wipe out any settings that may have been created
                    lock(_settingsLock) {
                        var names = _settings.Select(s => s.MorphName).ToList();
                        foreach(var n in names) {
                            DeleteMorphSettings(n);
                        }
                    }

                    var newSettings = new List<MorphSettings>();
                    foreach(SimpleJSON.JSONClass c in jc[JSON_CONFIG_PARENT].AsArray) {
                        var name = c[JSON_CONFIG_NAME];
                        var isInwardMorph = c[JSON_CONFIG_REVERSE]?.AsBool ?? false;
                        var inwardMax = c[JSON_CONFIG_IN_MAX]?.AsFloat ?? 0.25f;
                        var outwardMax = c[JSON_CONFIG_OUT_MAX]?.AsFloat ?? 0.25f;
                        var inwardExaggeration = c[JSON_CONFIG_IN_EXAG]?.AsFloat ?? 0;
                        var outwardExaggeration = c[JSON_CONFIG_OUT_EXAG]?.AsFloat ?? 0;
                        var friction = c[JSON_CONFIG_FRICTION]?.AsFloat ?? 1;
                        var enabled = c[JSON_CONFIG_ENABLED]?.AsBool ?? true;

                        var setting = MorphSettings.Create(
                            this, name,
                            enabled: enabled,
                            isInwardMorph: isInwardMorph,
                            friction: friction,
                            inwardMax: inwardMax,
                            outwardMax: outwardMax,
                            inwardExaggeration: inwardExaggeration,
                            outwardExaggeration: outwardExaggeration
                        );
                        if(setting != null) {
                            newSettings.Add(setting);
                        }
                    }
                    lock(_settingsLock) {
                        _settings = newSettings;
                    }
                }
                else {
                    // legacy settings? override some defaults
                    // "id" : "plugin#1_LFE.ExtraAutoGenitals", 
                    // "Labia minora-style1" : "false", 
                    // "Labia majora-relaxation" : "false", 
                    // "Friction" : "0.6275478", 
                    // "Inward Exaggeration" : "0.06887849", 
                    // "Outward Exaggeration" : "0.08138113"

                    lock(_settingsLock) {
                        if(jc.HasKey(JSON_V1_FRICTION)) {
                            foreach(var s in _settings) {
                                s.Friction.val = jc[JSON_V1_FRICTION].AsFloat;
                            }
                        }
                        if(jc.HasKey(JSON_V1_IN_EXAG)) {
                            foreach(var s in _settings) {
                                s.InwardExaggeration.val = jc[JSON_V1_IN_EXAG].AsFloat;
                            }
                        }
                        if(jc.HasKey(JSON_V1_OUT_EXAG)) {
                            foreach(var s in _settings) {
                                s.OutwardExaggeration.val = jc[JSON_V1_OUT_EXAG].AsFloat;
                            }
                        }
                        foreach(var kv in defaultEnabled) {
                            foreach(var s in _settings) {
                                if(jc.HasKey(s.MorphName)) {
                                    s.Enabled.val = jc[s.MorphName].AsBool;
                                }
                            }
                        }
                    }
                }

            }
            catch(Exception e) {
                SuperController.LogError($"{e}");
            }
            finally {
                _initComplete = true;
            }
        }

        private void SetupDefaultSettings() {
            // create default settings
            lock(_settingsLock) {
                if(_settings.Count == 0) {
                    _settings.Add(MorphSettings.Create(this, "Labia minora-size",
                        enabled: defaultEnabled["Labia minora-size"],
                        isInwardMorph: false, inwardMax: 0.7f, outwardMax: 2f, friction: defaultFriction, inwardExaggeration: defaultInwardExaggeration, outwardExaggeration: defaultOutwardExaggeration));
                    _settings.Add(MorphSettings.Create(this, "Labia minora-style1",
                        enabled: defaultEnabled["Labia minora-style1"],
                        isInwardMorph: false, inwardMax: 0.7f, outwardMax: 2f, friction: defaultFriction, inwardExaggeration: defaultInwardExaggeration, outwardExaggeration: defaultOutwardExaggeration));
                    _settings.Add(MorphSettings.Create(this, "Labia minora-exstrophy",
                        enabled: defaultEnabled["Labia minora-exstrophy"],
                        isInwardMorph: true, inwardMax: 0.1f, outwardMax: 1f, friction: defaultFriction, inwardExaggeration: defaultInwardExaggeration, outwardExaggeration: defaultOutwardExaggeration));
                    _settings.Add(MorphSettings.Create(this, "Labia majora-relaxation",
                        enabled: defaultEnabled["Labia majora-relaxation"],
                        isInwardMorph: false, inwardMax: 1f, outwardMax: 0f, friction: defaultFriction, inwardExaggeration: defaultInwardExaggeration, outwardExaggeration: defaultOutwardExaggeration));
                    _settings.Add(MorphSettings.Create(this, "Gen_Innie",
                        enabled: defaultEnabled["Gen_Innie"],
                        isInwardMorph: true, inwardMax: 0.10f, outwardMax: 0.25f, friction: defaultFriction, inwardExaggeration: defaultInwardExaggeration, outwardExaggeration: defaultOutwardExaggeration)); // TODO: easing p^3
                    _settings.Add(MorphSettings.Create(this, "Gens In - Out",
                        enabled: defaultEnabled["Gens In - Out"],
                        isInwardMorph: true, inwardMax: 1.0f, outwardMax: 0f, friction: defaultFriction, inwardExaggeration: defaultInwardExaggeration, outwardExaggeration: defaultOutwardExaggeration));
                }
                _settings = _settings.Where(a => a != null).ToList(); // remove any null entries
            }
        }

        // saving scene
        public override JSONClass GetJSON(bool includePhysical = true, bool includeAppearance = true, bool forceStore = false) {
            _initComplete = false;
            var json = base.GetJSON(includePhysical, includeAppearance, true);

            var configs = new SimpleJSON.JSONArray();
            lock(_settingsLock) {
                foreach(var s in _settings) {
                    var config = new SimpleJSON.JSONClass();
                    config[JSON_CONFIG_NAME] = s.MorphName;
                    config[JSON_CONFIG_ENABLED].AsBool = s.Enabled.val;
                    config[JSON_CONFIG_FRICTION].AsFloat = s.Friction.val;
                    config[JSON_CONFIG_IN_EXAG].AsFloat = s.InwardExaggeration.val;
                    config[JSON_CONFIG_OUT_EXAG].AsFloat = s.OutwardExaggeration.val;
                    config[JSON_CONFIG_IN_MAX].AsFloat = s.InwardMax.val;
                    config[JSON_CONFIG_OUT_MAX].AsFloat = s.OutwardMax.val;
                    config[JSON_CONFIG_REVERSE].AsBool = s.Reverse.val;
                    configs.Add(config);
                }
            }
            json[JSON_CONFIG_PARENT] = configs;
            _initComplete = true;
            return json;
        }


        void OnDestroy()
        {
            foreach (var a in _settings) { a?.Animator?.Morph?.SetDefaultValue(); }
        }

        void OnDisable() {
            foreach (var a in _settings) { a?.Animator?.Morph?.SetDefaultValue(); }
        }

    }

    public class LabiaAnimator
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
        public float InwardExaggeration { get; set; }
        public float OutwardMax { get; set; }
        public float OutwardExaggeration { get; set; }
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
                SuperController.LogError(e.Message);
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
            var morphTargetMin = MorphRestingValue - (IsInwardMorph ? OutwardMax + OutwardExaggeration : InwardMax + InwardExaggeration);
            var morphTargetMax = MorphRestingValue + (IsInwardMorph ? InwardMax + InwardMax : OutwardMax + OutwardExaggeration);
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

    public class MorphSettings {
        public JSONStorableFloat InwardMax;
        public JSONStorableFloat InwardExaggeration;
        public JSONStorableFloat OutwardMax;
        public JSONStorableFloat OutwardExaggeration;
        public JSONStorableFloat Friction;
        public JSONStorableBool Reverse;
        public JSONStorableBool Enabled;
        public string MorphName;
        public LabiaAnimator Animator = null;

        private ExtraAutoGenitals _plugin;
        private Color transparent = new Color(0, 0, 0, 0.1f);
        private List<UIDynamic> _uiElements = new List<UIDynamic>();

        private MorphSettings(ExtraAutoGenitals plugin, DAZMorph morph) {
            _plugin = plugin;

            MorphName = morph.displayName;

            Animator = LabiaAnimator.Create(plugin.containingAtom, morph.displayName, isInwardMorph: false, inwardMax: morph.min, outwardMax: morph.max);

            Enabled = new JSONStorableBool($"{morph.displayName}", true, (val) => {
                Animator.Enabled = val;
                Animator.Morph.SetDefaultValue();
            });
            InwardExaggeration = new JSONStorableFloat(
                "Inward Exaggeration", 0,
                (val) => {
                    Animator.InwardExaggeration = val;
                },
                0, 5);


            OutwardExaggeration = new JSONStorableFloat(
                "Outward Exaggeration", 0,
                (val) => {
                    Animator.OutwardExaggeration = val;
                },
                0, 5);

            InwardMax = new JSONStorableFloat("Inward Max", morph.min, (val) => {
                Animator.InwardMax = val;
            }, -5, 5);
            OutwardMax = new JSONStorableFloat("Outward Max", morph.max, (val) => {
                Animator.OutwardMax = val;
            }, -5, 5);
            Reverse = new JSONStorableBool("Reverse Direction?", false, (val) => {
                Animator.IsInwardMorph = val;
            });
            Friction = new JSONStorableFloat("Friction", 1, 0, 1);

            // TODO: easing

            var _uiOnOffToggle = _plugin.CreateToggle(Enabled);
            _uiOnOffToggle.backgroundColor = transparent;
            _uiOnOffToggle.labelText.fontStyle = FontStyle.Bold;
            var _uiS1 = _plugin.CreateSpacer(rightSide: true);
            _uiS1.height = _uiOnOffToggle.height;

            var _uiExaggerationIn  = _plugin.CreateSlider(InwardExaggeration);
            var _uiExaggerationOut = _plugin.CreateSlider(OutwardExaggeration, rightSide: true);

            var _uiMinMorphValue = _plugin.CreateSlider(InwardMax);
            var _uiMaxMorphValue = _plugin.CreateSlider(OutwardMax, rightSide: true);

            var _uiFriction = _plugin.CreateSlider(Friction);
            var _uiReverse = _plugin.CreateToggle(Reverse, rightSide: true);
            var _uiDelete = _plugin.CreateButton("Delete", rightSide: true);
            _uiDelete.buttonColor = Color.red;
            _uiDelete.button.onClick.AddListener(() => {
                _plugin.DeleteMorphSettings(MorphName);
            });

            var _uiS2 = _plugin.CreateSpacer();
            _uiS2.height = 50;
            var _uiS3 = _plugin.CreateSpacer(rightSide: true);
            _uiS3.height = 50 + 5;

            _uiElements.Add(_uiOnOffToggle);
            _uiElements.Add(_uiExaggerationIn);
            _uiElements.Add(_uiExaggerationOut);
            _uiElements.Add(_uiMinMorphValue);
            _uiElements.Add(_uiMaxMorphValue);
            _uiElements.Add(_uiFriction);
            _uiElements.Add(_uiReverse);
            _uiElements.Add(_uiDelete);
            _uiElements.Add(_uiS1);
            _uiElements.Add(_uiS2);
            _uiElements.Add(_uiS3);

            Animator.Morph.SetDefaultValue();
        }

        public static MorphSettings Create(ExtraAutoGenitals plugin, string morphName, bool isInwardMorph = false, float inwardMax = 0.5f, float outwardMax = 0.5f, float inwardExaggeration = 0, float outwardExaggeration = 0, float friction = 1, bool enabled = true) {
            try
            {
                Atom atom = plugin.containingAtom;
                DAZMorph morph = ((DAZCharacterSelector)atom.GetStorableByID("geometry")).morphsControlUI.GetMorphByDisplayName(morphName);
                if(morph != null) {
                    var s = new MorphSettings(plugin, morph);
                    s.Reverse.val = isInwardMorph;
                    s.Reverse.defaultVal = isInwardMorph;

                    s.InwardMax.val = inwardMax;
                    s.InwardMax.defaultVal = inwardMax;

                    s.OutwardMax.val = outwardMax;
                    s.OutwardMax.defaultVal = outwardMax;

                    s.InwardExaggeration.val = inwardExaggeration;
                    s.InwardExaggeration.defaultVal = inwardExaggeration;

                    s.OutwardExaggeration.val = outwardExaggeration;
                    s.OutwardExaggeration.defaultVal = outwardExaggeration;

                    s.Friction.val = friction;
                    s.Friction.defaultVal = friction;

                    s.Enabled.val = enabled;
                    s.Enabled.defaultVal = enabled;
                    return s;
                }
                else {
                    return null;
                }
            }
            catch (Exception e)
            {
                SuperController.LogError(e.Message);
                return null;
            }

        }

        public void Destroy() {
            Enabled.val = false;
            foreach(var u in _uiElements) {
                if(u is UIDynamicSlider) {
                    _plugin.RemoveSlider(u as UIDynamicSlider);
                }
                else if(u is UIDynamicButton) {
                    _plugin.RemoveButton(u as UIDynamicButton);
                }
                else if(u is UIDynamicToggle) {
                    _plugin.RemoveToggle(u as UIDynamicToggle);
                }
                else {
                    _plugin.RemoveSpacer(u);
                }
            }
        }
    }
}

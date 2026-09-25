using System;
using System.Globalization;

namespace RossQoL.Core.Combat
{
    /// <summary>
    /// The player's SECOND guardian-power slot and its cooldown. Slot 1 stays
    /// vanilla's own selected power (SetGuardianPower) -- stored by vanilla,
    /// fired by vanilla's key -- so this holds only the one extra power, which
    /// fires on its own key and runs its own cooldown. Pure: no game types. The
    /// power is a StatusEffect name (e.g. "GP_Eikthyr").
    ///
    /// Serialised as <c>name</c> or <c>name|cooldown</c> (the cooldown is left
    /// off when the power is ready), a single <c>m_customData</c> value the
    /// character save carries for free.
    /// </summary>
    public sealed class SecondPower
    {
        public string Power { get; private set; } = "";

        private float _cooldown;

        public bool HasPower => !string.IsNullOrEmpty(Power);

        /// <summary>Assign the slot. Returns true when it actually changed. A new power starts ready.</summary>
        public bool Set(string power)
        {
            power = power ?? "";
            if (power == Power) return false;
            Power = power;
            _cooldown = 0f;
            return true;
        }

        /// <summary>Empty the slot. Returns true when there was a power to clear.</summary>
        public bool Clear()
        {
            if (!HasPower) return false;
            Power = "";
            _cooldown = 0f;
            return true;
        }

        public void StartCooldown(float seconds)
        {
            if (seconds > 0f) _cooldown = seconds;
        }

        public float Remaining => _cooldown > 0f ? _cooldown : 0f;

        public bool IsReady => _cooldown <= 0f;

        public void Tick(float dt)
        {
            if (dt <= 0f || _cooldown <= 0f) return;
            _cooldown -= dt;
            if (_cooldown < 0f) _cooldown = 0f;
        }

        public string Serialize()
        {
            if (!HasPower) return "";
            return Remaining > 0f
                ? Power + "|" + Remaining.ToString(CultureInfo.InvariantCulture)
                : Power;
        }

        /// <summary>
        /// Parse a stored value. <paramref name="isValid"/>, when given, drops a
        /// name the game no longer knows (a removed-mod power, a hand-edit) so no
        /// ghost slot is left behind -- the same guard the loadout used.
        /// </summary>
        public static SecondPower Parse(string data, Func<string, bool> isValid = null)
        {
            var slot = new SecondPower();
            if (string.IsNullOrEmpty(data)) return slot;

            int bar = data.IndexOf('|');
            string name = (bar < 0 ? data : data.Substring(0, bar)).Trim();
            if (name.Length == 0) return slot;
            if (isValid != null && !isValid(name)) return slot;

            slot.Power = name;
            if (bar >= 0
                && float.TryParse(data.Substring(bar + 1).Trim(),
                    NumberStyles.Float, CultureInfo.InvariantCulture, out float secs)
                && secs > 0f)
            {
                slot._cooldown = secs;
            }
            return slot;
        }
    }
}

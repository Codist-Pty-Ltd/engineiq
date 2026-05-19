"use client";

type ToggleRowProps = {
  label: string;
  description?: string;
  checked: boolean;
  disabled?: boolean;
  onChange: (checked: boolean) => void;
};

export function ToggleRow({ label, description, checked, disabled, onChange }: ToggleRowProps) {
  return (
    <div className="eq-setting-row">
      <div>
        <div className="eq-setting-label">{label}</div>
        {description ? <div className="eq-setting-sub">{description}</div> : null}
      </div>
      <button
        type="button"
        role="switch"
        aria-checked={checked}
        aria-label={label}
        disabled={disabled}
        className="eq-toggle"
        onClick={() => onChange(!checked)}
      >
        <span className="eq-toggle-knob" />
      </button>
    </div>
  );
}

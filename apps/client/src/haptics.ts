import { Capacitor } from '@capacitor/core';
import { Haptics, ImpactStyle, NotificationType } from '@capacitor/haptics';

/**
 * Touch feedback: the native Taptic/vibration engine inside the iOS and Android apps, the
 * Vibration API in a mobile browser that has it, nothing elsewhere. Every call is fire-and-forget.
 */
const native = Capacitor.isNativePlatform();

function buzz(ms: number | number[]): void {
  try {
    navigator.vibrate?.(ms);
  } catch {
    // Some browsers throw outside a user gesture; feedback is optional.
  }
}

export const haptics = {
  /** One turret detent. */
  click(): void {
    if (native) void Haptics.selectionChanged().catch(() => undefined);
    else buzz(5);
  },
  /** The rifle going off. */
  recoil(): void {
    if (native) void Haptics.impact({ style: ImpactStyle.Heavy }).catch(() => undefined);
    else buzz(40);
  },
  /** Your round struck steel or the enemy, after `delay` seconds of flight. */
  hit(delay = 0): void {
    setTimeout(() => {
      if (native) void Haptics.impact({ style: ImpactStyle.Light }).catch(() => undefined);
      else buzz(15);
    }, delay * 1000);
  },
  /** You were hit. */
  struck(lethal: boolean): void {
    if (native) void Haptics.notification({ type: lethal ? NotificationType.Error : NotificationType.Warning }).catch(() => undefined);
    else buzz(lethal ? [80, 60, 160] : [80]);
  },
  /** A supersonic snap passed close by. */
  snap(): void {
    if (native) void Haptics.impact({ style: ImpactStyle.Medium }).catch(() => undefined);
    else buzz(20);
  },
};

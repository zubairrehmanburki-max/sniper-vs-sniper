// Taptic Engine feedback for the game (UIKit feedback generators).
#import <UIKit/UIKit.h>

extern "C" void SvsHaptic(int kind)
{
    dispatch_async(dispatch_get_main_queue(), ^{
        switch (kind)
        {
            case 0: { UISelectionFeedbackGenerator *g = [UISelectionFeedbackGenerator new]; [g selectionChanged]; break; }
            case 1: { UIImpactFeedbackGenerator *g = [[UIImpactFeedbackGenerator alloc] initWithStyle:UIImpactFeedbackStyleHeavy]; [g impactOccurred]; break; }
            case 2: { UIImpactFeedbackGenerator *g = [[UIImpactFeedbackGenerator alloc] initWithStyle:UIImpactFeedbackStyleRigid]; [g impactOccurred]; break; }
            case 3: { UINotificationFeedbackGenerator *g = [UINotificationFeedbackGenerator new]; [g notificationOccurred:UINotificationFeedbackTypeError]; break; }
            case 4: { UINotificationFeedbackGenerator *g = [UINotificationFeedbackGenerator new]; [g notificationOccurred:UINotificationFeedbackTypeWarning]; break; }
        }
    });
}

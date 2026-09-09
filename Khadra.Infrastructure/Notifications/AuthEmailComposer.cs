using System.Net;
using Khadra.Application.Common.Ports;
using Khadra.Domain.IdentityAccess;
using Khadra.Infrastructure.Configuration;
using Microsoft.Extensions.Options;

namespace Khadra.Infrastructure.Notifications;

/// <summary>
/// The five messages the platform sends about an account, in Arabic and English together.
/// </summary>
/// <remarks>
/// <para>
/// <b>Both languages in one message, Arabic first.</b> This is a Jordanian marketplace, and until
/// 2026-09-08 every one of these left in English only — a verification link a customer using the app
/// in Arabic could not read, which is the one email they must act on before they can do anything at
/// all. Pre-launch item 40.
/// </para>
/// <para>
/// Bilingual rather than chosen per recipient, deliberately. Choosing would need a language stored
/// on the account, and there is none: the console and the app both keep the reader's choice in their
/// own browser. Adding a column to pick a language would also mean picking WRONG for the invitation
/// emails, which go to somebody who has never used the platform and has expressed no preference. One
/// message that both readers can read is right for every case and needs nothing new.
/// </para>
/// <para>
/// The Arabic half carries <c>dir="rtl"</c> on its own block, not on the document: the English half
/// below it must stay left-to-right, and a direction on the body would flip the link text and the
/// punctuation with it.
/// </para>
/// <para>
/// The link appears ONCE, between the two halves. Two links to the same single-use token is an
/// invitation to click the second after the first has consumed it, and to read the failure as the
/// platform being broken.
/// </para>
/// </remarks>
internal sealed class AuthEmailComposer(IOptions<AppOptions> options) : IAuthEmailComposer
{
    private readonly string _clientBaseUrl = options.Value.ClientBaseUrl.TrimEnd('/');

    public EmailMessage EmailVerification(User user, string rawToken)
    {
        ArgumentNullException.ThrowIfNull(user);
        return Compose(
            user,
            Link("verify-email", rawToken),
            subject: "أكِّد بريدك الإلكتروني · Verify your Khadra email address",
            arabicBody: "أكِّد بريدك الإلكتروني لتبدأ استخدام خضرا.",
            arabicAction: "تأكيد البريد الإلكتروني",
            arabicFooter: "إن لم تكن قد أنشأت حساباً، تجاهل هذه الرسالة.",
            englishBody: "Confirm your email address to start using Khadra.",
            englishAction: "Verify my email",
            englishFooter: "If you did not create an account, ignore this message.");
    }

    public EmailMessage PasswordReset(User user, string rawToken)
    {
        ArgumentNullException.ThrowIfNull(user);
        return Compose(
            user,
            Link("reset-password", rawToken),
            subject: "إعادة تعيين كلمة المرور · Reset your Khadra password",
            arabicBody: "وصلنا طلب لإعادة تعيين كلمة المرور الخاصة بك.",
            arabicAction: "اختر كلمة مرور جديدة",
            arabicFooter: "إن لم تطلب ذلك، تجاهل هذه الرسالة؛ وتبقى كلمة مرورك كما هي.",
            englishBody: "We received a request to reset your password.",
            englishAction: "Choose a new password",
            englishFooter: "If you did not request this, ignore this message; your password stays unchanged.");
    }

    public EmailMessage PasswordChanged(User user)
    {
        ArgumentNullException.ThrowIfNull(user);
        return Compose(
            user,
            link: null,
            subject: "تم تغيير كلمة المرور · Your Khadra password was changed",
            arabicBody: "تم تغيير كلمة مرورك للتو، وتم تسجيل الخروج من كل الجلسات الأخرى.",
            arabicAction: null,
            arabicFooter: "إن لم تكن أنت، فأعد تعيين كلمة المرور فوراً وتواصل مع الدعم.",
            englishBody: "Your password was just changed and all other sessions were signed out.",
            englishAction: null,
            englishFooter: "If this was not you, reset your password immediately and contact support.");
    }

    public EmailMessage AdminInvitation(User user, string rawToken)
    {
        ArgumentNullException.ThrowIfNull(user);
        return Compose(
            user,
            // Same route the staff invitation uses: one screen redeems both, told apart by the token.
            Link("accept-invitation", rawToken),
            subject: "دعوة لإدارة خضرا · You have been invited to administer Khadra",
            arabicBody:
                "مُنِحت حساب مشرف على خضرا. يراجع المشرفون مكاتب التأجير، ويحسمون النزاعات، "
                + "ويطّلعون على كل حجز على المنصة.",
            arabicAction: "اقبل الدعوة واختر كلمة مرورك",
            arabicFooter:
                "إن لم تكن تتوقع هذه الرسالة، تجاهلها وأبلغ من يدير المنصة؛ ولا يُفعَّل شيء حتى تقبل.",
            englishBody:
                "You have been given an administrator account on Khadra. Administrators review rental "
                + "offices, decide disputes and can see every booking on the platform.",
            englishAction: "Accept the invitation and choose your password",
            englishFooter:
                "If you were not expecting this, ignore this message and tell whoever runs the "
                + "platform; nothing is set up until you accept.");
    }

    public EmailMessage EmployeeInvitation(User user, string dealerName, string rawToken)
    {
        ArgumentNullException.ThrowIfNull(user);
        ArgumentException.ThrowIfNullOrWhiteSpace(dealerName);
        var business = WebUtility.HtmlEncode(dealerName);

        return Compose(
            user,
            Link("accept-invitation", rawToken),
            subject: $"{dealerName} دعاك إلى خضرا · {dealerName} has invited you to Khadra",
            arabicBody:
                $"أضافك {business} موظفاً على خضرا. ستتمكن من الاطّلاع على طلبات حجز المكتب والرد عليها.",
            arabicAction: "اقبل الدعوة واختر كلمة مرورك",
            arabicFooter: "إن لم تكن تتوقع هذه الرسالة، تجاهلها؛ ولا يُفعَّل شيء حتى تقبل.",
            englishBody:
                $"{business} has added you as a member of staff on Khadra. You will be able to see "
                + "and answer the office's booking requests.",
            englishAction: "Accept the invitation and choose your password",
            englishFooter:
                "If you were not expecting this, you can ignore this message; nothing is set up "
                + "until you accept.");
    }

    /// <summary>
    /// One message carrying the same thing twice: Arabic, the action, then English.
    /// </summary>
    /// <remarks>
    /// The plain-text alternative is not an afterthought. A mail client that refuses HTML, and every
    /// spam filter that reads the text part to decide whether a message is worth delivering, sees
    /// only this — and a verification email that does not arrive is indistinguishable, to the person
    /// waiting for it, from an account that was never created.
    /// </remarks>
    private static EmailMessage Compose(
        User user,
        string? link,
        string subject,
        string arabicBody,
        string? arabicAction,
        string arabicFooter,
        string englishBody,
        string? englishAction,
        string englishFooter)
    {
        var name = WebUtility.HtmlEncode(user.Name.Value);

        var html =
            $"""<div dir="rtl" lang="ar" style="text-align:right"><p>مرحباً {name}،</p><p>{arabicBody}</p>"""
            + (link is null || arabicAction is null
                ? string.Empty
                : $"""<p><a href="{link}">{arabicAction}</a></p>""")
            + $"<p>{arabicFooter}</p></div>"
            + """<hr style="border:none;border-top:1px solid #ddd;margin:20px 0">"""
            + $"""<div dir="ltr" lang="en"><p>Hi {name},</p><p>{englishBody}</p>"""
            + (link is null || englishAction is null
                ? string.Empty
                : $"""<p><a href="{link}">{englishAction}</a></p>""")
            + $"<p>{englishFooter}</p></div>";

        var text =
            $"مرحباً {user.Name.Value}،\n\n{arabicBody}\n"
            + (link is null ? string.Empty : $"{link}\n")
            + $"\n{arabicFooter}\n\n"
            + "----------\n\n"
            + $"Hi {user.Name.Value},\n\n{englishBody}\n"
            + (link is null ? string.Empty : $"{link}\n")
            + $"\n{englishFooter}";

        return new EmailMessage(user.Email.Value, user.Name.Value, subject, html, text);
    }

    private string Link(string route, string rawToken) =>
        $"{_clientBaseUrl}/{route}?token={Uri.EscapeDataString(rawToken)}";
}

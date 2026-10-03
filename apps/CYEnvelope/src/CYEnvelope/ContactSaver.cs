namespace CYEnvelope;

public enum SaveChoice { Add, Overwrite, Cancel }

// The customer already has saved addresses/phones and the printed one matches none of them.
public sealed record SaveRequest(bool IsAddress, string NewValue, string ExistingLabel, int ExistingCount);

public sealed record SavedContact(Contact Contact, ContactAddress Address, ContactPhone? Phone);

// Printing saves the customer: the single place that decides how printed data joins the database.
public static class ContactSaver
{
    // picked*: what the user chose from the saved lists (used as the overwrite target).
    // ask: asks Add / Overwrite / Cancel when the value is new for a customer that already has some.
    public static SavedContact Save(Repository repository, Contact? picked, ContactAddress? pickedAddress,
        ContactPhone? pickedPhone, PrintData data, Func<SaveRequest, SaveChoice> ask)
    {
        var name = Names.Normalize(data.Recipient);
        // Always merge into the stored record: other windows may have edited or deleted the picked copy.
        var contact = picked is null ? null : repository.GetContact(picked.Id);
        contact ??= repository.FindByName(name).FirstOrDefault() ?? new Contact { Name = name };
        if (contact.Name.Length == 0) contact.Name = name;

        var address = contact.Addresses.FirstOrDefault(x => Postal.SameAddress(x.Value, data.Address));
        if (address is null)
        {
            var target = contact.Addresses.FirstOrDefault(x => x.Id == pickedAddress?.Id)
                         ?? contact.Addresses.FirstOrDefault(x => x.Id == contact.LastAddressId)
                         ?? contact.Addresses.LastOrDefault();
            var choice = target is null ? SaveChoice.Add
                : ask(new(true, data.Address, $"{target.Label}｜{target.Value}", contact.Addresses.Count));
            if (choice == SaveChoice.Cancel) throw new OperationCanceledException();
            if (choice == SaveChoice.Overwrite && target is not null) address = target;
            else
            {
                address = new ContactAddress { Label = $"地址{contact.Addresses.Count + 1}" };
                contact.Addresses.Add(address);
            }
            address.Value = data.Address.Trim();
        }
        address.PostalCode = data.PostalCode;
        contact.LastAddressId = address.Id;

        ContactPhone? phone = null;
        if (data.Phone.Length > 0)
        {
            var number = PhoneFormatting.Format(data.Phone);
            phone = contact.Phones.FirstOrDefault(x => x.Display == number);
            if (phone is null)
            {
                var target = contact.Phones.FirstOrDefault(x => x.Id == pickedPhone?.Id)
                             ?? contact.Phones.FirstOrDefault(x => x.Id == address.LastPhoneId)
                             ?? contact.Phones.LastOrDefault();
                var choice = target is null ? SaveChoice.Add
                    : ask(new(false, number, target.Display, contact.Phones.Count));
                if (choice == SaveChoice.Cancel) throw new OperationCanceledException();
                if (choice == SaveChoice.Overwrite && target is not null) phone = target;
                else { phone = new ContactPhone(); contact.Phones.Add(phone); }
                var parts = number.Split(" #", 2);
                phone.Number = parts[0];
                phone.Extension = parts.Length == 2 ? parts[1] : "";
            }
            address.LastPhoneId = phone.Id;
        }
        contact.LastDeliveryIds = data.DeliveryIds;
        repository.SaveContact(contact);
        return new SavedContact(contact, address, phone);
    }
}

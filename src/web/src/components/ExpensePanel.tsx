import { useState } from 'react'
import type { FormEvent } from 'react'
import { ALL_EXPENSE_CATEGORIES } from '../api/api-types'
import type {
  BalanceResponse,
  CreateExpenseRequest,
  ExpenseCategory,
  ExpenseResponse,
  MemberResponse,
} from '../api/api-types'
import { formatMoney, parseMoney } from '../api/money'
import { expenseCategoryLabel, shortDate } from '../api/labels'
import { formatDayLabel } from '../itinerary/tripDates'
import { ButtonBusy, Spinner } from './Spinner'
import { IconClose } from './icons'

interface ExpensePanelProps {
  expenses: ExpenseResponse[]
  balance: BalanceResponse | undefined
  members: MemberResponse[]
  myMemberId: string
  currency: string
  currencyExponent: number
  deletingId: string | null
  onDelete: (expenseId: string) => void
}

/**
 * Who paid, who owes, and the smallest set of payments that squares it up.
 *
 * Every amount here is an integer number of minor units end to end; the only
 * conversion to a decimal happens inside formatMoney, on the way to the screen.
 */
export function ExpensePanel({
  expenses,
  balance,
  members,
  myMemberId,
  currency,
  currencyExponent,
  deletingId,
  onDelete,
}: ExpensePanelProps) {
  const nameOf = (memberId: string) =>
    members.find((member) => member.id === memberId)?.displayName ?? 'Ai đó'

  return (
    <div className="expenses">
      <section className="balance-card">
        <h3>Số dư</h3>

        {balance === undefined ? (
          <p className="search-hint inline-busy" role="status">
            <Spinner />
            Đang tính…
          </p>
        ) : (
          <>
            {/*
              The settlement first, in the largest type on the card. Nobody
              opens a split-the-bill screen to learn the group total — they
              open it to find out what they owe and to whom. That answer used
              to be the smallest, lowest-contrast line here, under a 32px
              vanity number.
            */}
            {balance.transfers.length === 0 ? (
              <p className="settle-none">Không ai nợ ai 🎉</p>
            ) : (
              <ul className="settle-list" aria-label="Cần thanh toán">
                {balance.transfers.map((transfer, index) => (
                  <li key={index}>
                    <span className="settle-who">
                      {nameOf(transfer.fromMemberId)} → {nameOf(transfer.toMemberId)}
                    </span>
                    <strong className="settle-amount">
                      {formatMoney(transfer.amount, currency, currencyExponent)}
                    </strong>
                  </li>
                ))}
              </ul>
            )}

            <ul className="balance-list">
              {balance.balances.map((entry) => (
                <li
                  key={entry.memberId}
                  // The first question anybody asks this card is "what about
                  // me", and four similar names in a column do not answer it.
                  className={entry.memberId === myMemberId ? 'is-me' : undefined}
                  data-testid={`balance-${entry.memberId}`}
                >
                  <span>{nameOf(entry.memberId)}{entry.memberId === myMemberId && ' (bạn)'}</span>
                  {/*
                    A solid chip rather than coloured text straight onto the
                    gradient: the same words measured 3.6:1 at the light end of
                    the gradient and passed only at the dark end, so contrast
                    depended on where the text happened to land.
                  */}
                  <span
                    className={
                      entry.net > 0 ? 'net positive' : entry.net < 0 ? 'net negative' : 'net'
                    }
                  >
                    {entry.net === 0
                      ? 'đã đủ'
                      : entry.net > 0
                        ? `được nhận ${formatMoney(entry.net, currency, currencyExponent)}`
                        : `còn nợ ${formatMoney(-entry.net, currency, currencyExponent)}`}
                  </span>
                </li>
              ))}
            </ul>

            <p className="balance-total">
              Tổng chi <strong>{formatMoney(balance.totalSpent, currency, currencyExponent)}</strong>
            </p>
          </>
        )}
      </section>

      <section>
        <h3>Các khoản đã chi ({expenses.length})</h3>

        {expenses.length === 0 ? (
          <p className="empty-state small">Chưa có khoản chi nào.</p>
        ) : (
          <ul className="expense-list">
            {expenses.map((expense) => (
              <li key={expense.id} data-testid={`expense-${expense.id}`}>
                <div className="expense-body">
                  <span className="expense-title">{expense.title}</span>
                  <span className="expense-meta">
                    {shortDate(expense.date)} · {expenseCategoryLabel(expense.category)} ·{' '}
                    {nameOf(expense.paidByMemberId)} trả
                    {expense.splitType === 'Custom' && ' · chia tuỳ chỉnh'}
                  </span>
                </div>
                <span className="expense-amount">
                  {formatMoney(expense.amount, currency, currencyExponent)}
                </span>
                <button
                  type="button"
                  className="place-delete"
                  onClick={() => onDelete(expense.id)}
                  disabled={deletingId === expense.id}
                  aria-label={`Xoá ${expense.title}`}
                >
                  {deletingId === expense.id ? <Spinner /> : <IconClose />}
                </button>
              </li>
            ))}
          </ul>
        )}
      </section>

    </div>
  )
}

/**
 * Exported so the workspace can lift it into a sheet: a form that is always
 * expanded pushes the balance and the list of what has been spent — the two
 * things somebody opens this tab to read — below the fold.
 */
export function AddExpenseForm(props: {
  members: MemberResponse[]
  myMemberId: string
  currency: string
  currencyExponent: number
  tripDays: string[]
  pending: boolean
  submitError: string | null
  onAdd: (body: CreateExpenseRequest) => void
}) {
  return <ExpenseForm {...props} />
}

function ExpenseForm({
  members,
  myMemberId,
  currency,
  currencyExponent,
  tripDays,
  pending,
  submitError,
  onAdd,
}: {
  members: MemberResponse[]
  myMemberId: string
  currency: string
  currencyExponent: number
  tripDays: string[]
  pending: boolean
  submitError: string | null
  onAdd: (body: CreateExpenseRequest) => void
}) {
  const [title, setTitle] = useState('')
  const [amount, setAmount] = useState('')
  const [paidBy, setPaidBy] = useState(myMemberId)
  const [date, setDate] = useState(tripDays[0] ?? '')
  const [category, setCategory] = useState<ExpenseCategory>('Food')
  const [localError, setLocalError] = useState<string | null>(null)

  function handleSubmit(event: FormEvent) {
    event.preventDefault()
    setLocalError(null)

    const minorUnits = parseMoney(amount, currencyExponent)

    // "Không đọc được" and "phải lớn hơn 0" are different problems, and telling
    // somebody who typed "1,500,000" that it must be greater than zero sends
    // them looking for the wrong mistake.
    if (minorUnits === null || Number.isNaN(minorUnits)) {
      setLocalError(
        currencyExponent === 0
          ? 'Số tiền chỉ gồm chữ số, ví dụ 200000 hoặc 200.000.'
          : 'Số tiền không hợp lệ.',
      )
      return
    }

    if (minorUnits <= 0) {
      setLocalError('Số tiền phải lớn hơn 0.')
      return
    }

    onAdd({
      title,
      amount: minorUnits,
      paidByMemberId: paidBy,
      date,
      category,
      // Equal only for now: custom splits need a per-member editor, and the
      // server validates the sum either way.
      splitType: 'Equal',
    })

    // Not cleared here — see the note in PlaceForm. A rejected submit used to
    // wipe the title and the amount, so a rounding complaint cost you both.
    // The sheet unmounts this form when the server accepts it.
  }

  const parsedAmount = parseMoney(amount, currencyExponent)
  const amountEcho =
    parsedAmount === null
      ? ''
      : Number.isNaN(parsedAmount)
        ? 'Không đọc được số tiền'
        : `= ${formatMoney(parsedAmount, currency, currencyExponent)}`

  return (
    // The sheet supplies the title; see the note in PlaceForm.
    <form className="expense-form" onSubmit={handleSubmit} aria-label="Thêm chi tiêu">
      <label>
        Nội dung
        <input value={title} onChange={(e) => setTitle(e.target.value)} required maxLength={120} />
      </label>

      <div className="row">
        <label>
          Số tiền
          <input
            value={amount}
            onChange={(e) => setAmount(e.target.value)}
            inputMode="numeric"
            placeholder="200000"
            required
            aria-describedby="expense-amount-echo"
          />
          {/*
            What the app read, shown while typing. A money field that quietly
            interprets separators has to say what it decided before the user
            commits to it, not after.
          */}
          <span className="field-echo" id="expense-amount-echo" aria-live="polite">
            {amountEcho}
          </span>
        </label>
        <label>
          Ngày
          <select value={date} onChange={(e) => setDate(e.target.value)}>
            {tripDays.map((day) => (
              <option key={day} value={day}>
                {formatDayLabel(day)}
              </option>
            ))}
          </select>
        </label>
      </div>

      <div className="row">
        <label>
          Ai trả
          <select value={paidBy} onChange={(e) => setPaidBy(e.target.value)}>
            {members.map((member) => (
              <option key={member.id} value={member.id}>
                {member.displayName}
              </option>
            ))}
          </select>
        </label>
        <label>
          Loại
          <select
            value={category}
            onChange={(e) => setCategory(e.target.value as ExpenseCategory)}
          >
            {ALL_EXPENSE_CATEGORIES.map((option) => (
              <option key={option} value={option}>
                {expenseCategoryLabel(option)}
              </option>
            ))}
          </select>
        </label>
      </div>

      {(localError ?? submitError) && (
        <p className="form-error" role="alert">
          {localError ?? submitError}
        </p>
      )}

      <button type="submit" disabled={pending}>
        {pending ? <ButtonBusy>Đang lưu…</ButtonBusy> : 'Thêm khoản chi'}
      </button>
    </form>
  )
}
